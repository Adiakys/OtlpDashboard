using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenTelemetryDashboard.Dashboards.Endpoints;

namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Endpoint routing for the Dashboards module. Returns a
/// <see cref="RouteGroupBuilder"/> so the host can chain authorization,
/// rate limiting, etc. — mirrors the <c>MapQueryApi</c> pattern from
/// <c>OpenTelemetryDashboard.Api</c>.
/// </summary>
public static class DashboardsEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mounts the dashboards CRUD under <c>/api/v1/dashboards</c>:
    /// <list type="bullet">
    ///   <item><c>GET    /api/v1/dashboards</c> — list all</item>
    ///   <item><c>GET    /api/v1/dashboards/{id}</c> — get by id</item>
    ///   <item><c>POST   /api/v1/dashboards</c> — create</item>
    ///   <item><c>PUT    /api/v1/dashboards/{id}</c> — update (optimistic concurrency)</item>
    ///   <item><c>DELETE /api/v1/dashboards/{id}</c> — delete (default protected)</item>
    /// </list>
    /// </summary>
    public static RouteGroupBuilder MapDashboards(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/dashboards").WithTags("Dashboards");

        group.MapGet(string.Empty, DashboardEndpoints.GetAllDashboardAsync).WithName("GetAllDashboards");
        group.MapGet("/{id}", DashboardEndpoints.GetDashboardByIdAsync).WithName("GetDashboardById");
        group.MapPost(string.Empty, DashboardEndpoints.PostDashboardAsync).WithName("AddDashboard");
        group.MapPut("/{id}", DashboardEndpoints.PutDashboardAsync).WithName("UpdateDashboard");
        group.MapDelete("/{id}", DashboardEndpoints.DeleteDashboardAsync).WithName("DeleteDashboard");

        return group;
    }

    /// <summary>
    /// Mounts the user-saved widget definitions CRUD plus the read-only
    /// library picker under <c>/api/v1/widgets</c>:
    /// <list type="bullet">
    ///   <item><c>GET    /api/v1/widgets/definitions</c> — list custom</item>
    ///   <item><c>GET    /api/v1/widgets/definitions/{id}</c> — get by id</item>
    ///   <item><c>POST   /api/v1/widgets/definitions</c> — create</item>
    ///   <item><c>PUT    /api/v1/widgets/definitions/{id}</c> — update</item>
    ///   <item><c>DELETE /api/v1/widgets/definitions/{id}</c> — delete</item>
    ///   <item><c>GET    /api/v1/widgets/libraries</c> — flat library catalog</item>
    /// </list>
    /// Pack-level operations (install/update/uninstall) live under
    /// <c>/api/v1/packs</c> via <see cref="MapPacks"/>.
    /// </summary>
    public static RouteGroupBuilder MapWidgets(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/widgets").WithTags("Widgets");

        group.MapGet("/definitions", WidgetEndpoints.GetAllDefinitionsAsync)
            .WithName("GetAllWidgetDefinitions");
        group.MapGet("/definitions/{id}", WidgetEndpoints.GetDefinitionByIdAsync)
            .WithName("GetWidgetDefinitionById");
        group.MapPost("/definitions", WidgetEndpoints.PostDefinitionAsync)
            .WithName("AddWidgetDefinition");
        group.MapPut("/definitions/{id}", WidgetEndpoints.PutDefinitionAsync)
            .WithName("UpdateWidgetDefinition");
        group.MapDelete("/definitions/{id}", WidgetEndpoints.DeleteDefinitionAsync)
            .WithName("DeleteWidgetDefinition");

        group.MapGet("/libraries", WidgetLibraryEndpoints.GetLibrariesAsync)
            .WithName("GetWidgetLibraries");

        return group;
    }

    /// <summary>
    /// Mounts the pack management surface under <c>/api/v1/packs</c>:
    /// <list type="bullet">
    ///   <item><c>GET    /api/v1/packs</c> — list installed packs</item>
    ///   <item><c>POST   /api/v1/packs/reload</c> — refresh registry cache</item>
    ///   <item><c>POST   /api/v1/packs/install</c> — clone + register a pack</item>
    ///   <item><c>POST   /api/v1/packs/{id}/update</c> — fetch + reset a git-installed pack</item>
    ///   <item><c>DELETE /api/v1/packs/{id}</c> — uninstall a pack from the runtime root</item>
    /// </list>
    /// </summary>
    public static RouteGroupBuilder MapPacks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1/packs").WithTags("Packs");

        group.MapGet(string.Empty, PackEndpoints.GetPacksAsync).WithName("GetPacks");
        group.MapPost("/reload", PackEndpoints.ReloadPacksAsync).WithName("ReloadPacks");
        group.MapPost("/install", PackEndpoints.InstallPackAsync).WithName("InstallPack");
        group.MapPost("/{id}/update", PackEndpoints.UpdatePackAsync).WithName("UpdatePack");
        group.MapDelete("/{id}", PackEndpoints.UninstallPackAsync).WithName("UninstallPack");

        return group;
    }
}
