using OpenTelemetryDashboard.Api;
using OpenTelemetryDashboard.Core;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Host.Authentication;
using OpenTelemetryDashboard.Host.Configuration;
using OpenTelemetryDashboard.Ingestion;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Aggregates every <c>AddXxx(...)</c> call that registers domain services
/// (telemetry pipeline, query API, dashboards module, auth, MCP, health
/// checks). Server-level concerns (Kestrel, gRPC, rate-limiting, storage)
/// live in their own setup classes alongside this one.
/// </summary>
internal static class DashboardServicesExtensions
{
    public static WebApplicationBuilder AddDashboardServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddRoutingCore();
        builder.Services.AddTelemetryCore(builder.Configuration);
        builder.Services.AddOtlpIngestion();
        builder.Services.AddTelemetryWriter();
        builder.Services.AddTelemetryRetention(builder.Configuration);

        builder.Services.AddQueryApi(builder.Configuration);
        builder.Services.AddDashboards(builder.Configuration);
        builder.Services.AddDashboardAuth(builder.Configuration);

        // Compose the full DashboardInfoDto once at boot. The endpoint resolves
        // it from DI and returns either the full record (authenticated) or a
        // redacted copy (anonymous). Centralising "what /info contains" here
        // keeps Api decoupled from Host/Persistence config types — Api only
        // knows about its own DTO, the Host (which sees everything) does the
        // composition.
        builder.Services.RegisterDashboardInfo();

        // MCP services are registered unconditionally; the SDK only becomes
        // reachable when MapDashboardMcp() is called below (gated by
        // Dashboard:Mcp:Enabled at endpoint-mapping time).
        builder.Services.AddDashboardMcp();

        builder.Services
            .AddHealthChecks()
            .AddTelemetrySinkHealthCheck();

        return builder;
    }
}
