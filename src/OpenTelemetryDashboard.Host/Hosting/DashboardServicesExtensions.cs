using OpenTelemetryDashboard.Api;
using OpenTelemetryDashboard.Core;
using OpenTelemetryDashboard.Dashboards;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Authentication;
using OpenTelemetryDashboard.Host.Configuration;
using OpenTelemetryDashboard.Host.ErrorHandling;
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
        // Uniform RFC 7807 error shape: validation/concurrency endpoints already
        // emit ProblemDetails; this pair extends the same shape to anything that
        // escapes an endpoint as an unhandled exception.
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        // CORS: default policy permits only the configured origins. Empty
        // default means no Access-Control-Allow-Origin header is issued and
        // browsers reject any cross-origin XHR — correct for the same-origin
        // SPA-served-by-host deployment. Operators with split deployments
        // (SPA on its own CDN, etc.) populate Dashboard:Cors:AllowedOrigins.
        //
        // Bind lazily via IConfigureOptions<CorsOptions> so the policy is
        // built when CorsOptions is first resolved (at request time), not
        // during AddXxx — that matters in tests, where WithWebHostBuilder
        // appends its in-memory config sources AFTER Program.cs runs.
        builder.Services
            .AddOptions<DashboardCorsOptions>()
            .Bind(builder.Configuration.GetSection(DashboardCorsOptions.SectionName));
        builder.Services.AddSingleton<IConfigureOptions<CorsOptions>, ConfigureCorsFromDashboard>();
        builder.Services.AddCors();

        builder.Services.AddRoutingCore();
        builder.Services.AddTelemetryCore(builder.Configuration);
        builder.Services.AddOtlpIngestion();
        builder.Services.AddTelemetryWriter();
        builder.Services.AddTelemetryRetention(builder.Configuration);

        builder.Services.AddQueryApi(builder.Configuration);
        builder.Services.AddDashboards(builder.Configuration);
        builder.Services.AddDashboardAuth(builder.Configuration, builder.Environment);

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
            .AddTelemetryDbConnectivityHealthCheck()
            .AddTelemetrySinkHealthCheck()
            .AddRetentionPostureHealthCheck()
            .AddCheck<AuthPostureHealthCheck>("auth-posture", HealthStatus.Degraded);

        return builder;
    }
}
