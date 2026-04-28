using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// HTTP handler for the public info endpoint. Wiring lives in
/// <see cref="QueryApiExtensions.MapDashboardInfo"/>.
/// </summary>
internal static class InfoEndpoints
{
    // Pinned at load time — the assembly metadata is immutable once loaded.
    // Using the Api assembly is deterministic across prod runs and tests
    // (WebApplicationFactory would otherwise leak the test runner's version
    // via GetEntryAssembly()).
    private static readonly string Version = ResolveVersion();

    public static Ok<DashboardInfoDto> GetInfo(HttpContext context, IOptions<DashboardInfoOptions> options)
    {
        // The endpoint is AllowAnonymous, so UseAuthentication populates
        // HttpContext.User whenever a valid bearer is present; callers
        // without a token (or with an invalid one) arrive unauthenticated
        // and don't get to see the build version.
        var authenticated = context.User.Identity?.IsAuthenticated == true;

        return TypedResults.Ok(new DashboardInfoDto(
            options.Value.ApplicationName,
            authenticated ? Version : null));
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(InfoEndpoints).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrEmpty(informational)) return informational;

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrEmpty(assemblyVersion) ? "unknown" : assemblyVersion;
    }
}
