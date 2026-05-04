using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using OpenTelemetryDashboard.Api.Contracts;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// HTTP handler for the public info endpoint. Wiring lives in
/// <see cref="QueryApiExtensions.MapDashboardInfo"/>.
/// <para>
/// The fully-populated <see cref="DashboardInfoDto"/> is registered as a
/// singleton by the Host (see <c>Program.cs</c>); this handler resolves
/// it from DI and either returns it as-is (authenticated) or a redacted
/// copy with the infra-shape fields cleared (anonymous). All composition
/// of "what /info contains" lives in the Host — Api just gates.
/// </para>
/// </summary>
internal static class InfoEndpoints
{
    public static Ok<DashboardInfoDto> GetInfo(HttpContext context, IOptions<DashboardInfoDto> full)
    {
        // The endpoint is AllowAnonymous, so UseAuthentication populates
        // HttpContext.User whenever a valid bearer is present; callers
        // without a token (or with an invalid one) arrive unauthenticated
        // and don't get to see anything beyond the application name.
        var authenticated = context.User.Identity?.IsAuthenticated == true;
        if (authenticated) return TypedResults.Ok(full.Value);

        return TypedResults.Ok(new DashboardInfoDto(full.Value.ApplicationName));
    }
}
