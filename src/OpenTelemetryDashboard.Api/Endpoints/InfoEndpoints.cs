using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Core.Common;

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
        // Defense in depth: gate on an explicit role rather than the looser
        // IsAuthenticated flag. If a future contributor wires a second auth
        // scheme (cookie, OIDC) where an anonymous visitor still ends up
        // with IsAuthenticated == true, the role check still keeps build
        // metadata, storage provider, retention windows and query limits
        // off the public surface.
        var authorized = context.User.IsInRole(AuthRoleNames.Browser);
        if (authorized) return TypedResults.Ok(full.Value);

        return TypedResults.Ok(new DashboardInfoDto(full.Value.ApplicationName));
    }
}
