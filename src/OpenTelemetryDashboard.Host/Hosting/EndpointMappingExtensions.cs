using OpenTelemetryDashboard.Api;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Host.Authentication;
using OpenTelemetryDashboard.Host.Configuration;
using OpenTelemetryDashboard.Ingestion;
using OpenTelemetryDashboard.Ingestion.Http;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Maps every HTTP / gRPC endpoint and applies the matching authorization +
/// rate-limit policies. Mutating routes inside dashboard/widget/pack groups
/// override these defaults from inside their own extension methods.
/// </summary>
internal static class EndpointMappingExtensions
{
    public static WebApplication MapDashboardEndpoints(this WebApplication app)
    {
        MapOtlpIngestion(app);
        MapReadApi(app);
        MapMcp(app);
        MapHealthAndSpaFallback(app);
        return app;
    }

    private static void MapOtlpIngestion(WebApplication app)
    {
        app.MapOtlpGrpcServices(conv => conv
            .RequireAuthorization(AuthServiceCollectionExtensions.OtlpIngestPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.OtlpIngest));

        app.MapOtlpHttpEndpoints()
            .RequireAuthorization(AuthServiceCollectionExtensions.OtlpIngestPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.OtlpIngest);
    }

    private static void MapReadApi(WebApplication app)
    {
        app.MapQueryApi()
            .RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.ReadApi);
        app.MapDashboards()
            .RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.ReadApi);
        app.MapWidgets()
            .RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.ReadApi);
        app.MapPacks()
            .RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.ReadApi);

        // /info is intentionally anonymous so the SPA can read the application
        // name on the login screen — but it's still rate-limited to keep an
        // unauthenticated client from polling the boot info endlessly.
        app.MapDashboardInfo().RequireRateLimiting(HostRateLimitPolicies.ReadApi);
    }

    private static void MapMcp(WebApplication app)
    {
        var enabled = app.Configuration.GetValue<bool>(
            $"{DashboardMcpOptions.SectionName}:{nameof(DashboardMcpOptions.Enabled)}");
        if (!enabled)
        {
            return;
        }

        app.MapDashboardMcp()
            .RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy)
            .RequireRateLimiting(HostRateLimitPolicies.ReadApi);
    }

    private static void MapHealthAndSpaFallback(WebApplication app)
    {
        app.MapHealthChecks("/healthz").AllowAnonymous();

        // SPA client-side routing: any non-API request that didn't match an
        // endpoint or a static file falls back to index.html, which then
        // hydrates Vue Router. The SPA shell itself is public so the eventual
        // login form can render.
        app.MapFallbackToFile("index.html");
    }
}
