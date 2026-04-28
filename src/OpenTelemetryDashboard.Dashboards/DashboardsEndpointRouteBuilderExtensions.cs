using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenTelemetryDashboard.Dashboards.Endpoints;

namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Endpoint routing for the Dashboards module. Mirrors the
/// <c>MapQueryApi</c> pattern from <c>OpenTelemetryDashboard.Api</c>: the
/// caller (Host) supplies the authorization policy name so the module
/// stays unaware of how the host configures auth.
/// </summary>
public static class DashboardsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mounts <c>GET /api/v1/dashboards/default</c> and
    /// <c>PUT /api/v1/dashboards/default</c>. When
    /// <paramref name="authorizationPolicy"/> is non-null, the group requires
    /// it (typically <c>"read-api"</c> to share the SPA's auth posture).
    /// </summary>
    public static RouteGroupBuilder MapDashboards(
        this IEndpointRouteBuilder endpoints,
        string? authorizationPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/dashboards").WithTags("Dashboards");

        if (!string.IsNullOrEmpty(authorizationPolicy))
        {
            group = group.RequireAuthorization(authorizationPolicy);
        }

        group.MapGet("/default", DashboardEndpoints.GetDefaultAsync).WithName("GetDefaultDashboard");
        group.MapPut("/default", DashboardEndpoints.SaveDefaultAsync).WithName("SaveDefaultDashboard");

        return group;
    }
}
