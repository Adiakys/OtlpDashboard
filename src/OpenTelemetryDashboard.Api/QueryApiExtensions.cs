using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Api.Endpoints;

namespace OpenTelemetryDashboard.Api;

/// <summary>
/// The entire public surface of the Query API module: DI registration
/// (<see cref="AddQueryApi"/>) and endpoint mapping
/// (<see cref="MapQueryApi"/>). Callers from the Host wire both in
/// <c>Program.cs</c>.
/// </summary>
public static class QueryApiExtensions
{
    /// <summary>
    /// Registers <see cref="QueryApiOptions"/> and JSON serializer settings
    /// shared by the Query API endpoints.
    /// </summary>
    public static IServiceCollection AddQueryApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<QueryApiOptions>()
            .Bind(configuration.GetSection(QueryApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<DashboardInfoOptions>()
            .Bind(configuration.GetSection(DashboardInfoOptions.SectionName));

        services.ConfigureHttpJsonOptions(o =>
        {
            // Preserve attribute keys verbatim (e.g. "service.name"); camelCase
            // would mangle them.
            o.SerializerOptions.DictionaryKeyPolicy = null;

            // Web defaults omit null-valued properties. The Query API exposes
            // optional fields (traceId, body, …) as explicit nulls so clients
            // see a stable shape.
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });

        return services;
    }

    /// <summary>
    /// Maps <c>GET /api/v1/logs</c>, <c>GET /api/v1/traces</c>, and
    /// <c>GET /api/v1/traces/{traceId}</c>. Handler bodies live in
    /// <see cref="LogsEndpoints"/> and <see cref="TracesEndpoints"/>.
    /// </summary>
    public static RouteGroupBuilder MapQueryApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v1").WithTags("Query");

        group.MapGet("/logs", LogsEndpoints.GetLogsAsync).WithName("GetLogs");
        group.MapGet("/logs/services", ServicesEndpoints.GetLogServicesAsync).WithName("GetLogServices");
        group.MapGet("/traces", TracesEndpoints.GetTracesAsync).WithName("GetTraces");
        group.MapGet("/traces/services", ServicesEndpoints.GetTraceServicesAsync).WithName("GetTraceServices");
        group.MapGet("/traces/{traceId}", TracesEndpoints.GetTraceAsync).WithName("GetTrace");
        group.MapGet("/metrics", MetricsEndpoints.ListInstruments).WithName("ListMetrics");
        group.MapGet("/metrics/points", MetricsEndpoints.GetPoints).WithName("GetMetricPoints");
        group.MapGet("/metrics/services", ServicesEndpoints.GetMetricServices).WithName("GetMetricServices");

        return group;
    }

    /// <summary>
    /// Maps the public <c>GET /api/v1/info</c> endpoint. Intentionally not
    /// mounted under <see cref="MapQueryApi"/> so it isn't captured by the
    /// <c>read-api</c> authorization policy — the SPA reads it at boot
    /// (before login) to display the configured application name.
    /// </summary>
    public static RouteHandlerBuilder MapDashboardInfo(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints
            .MapGet("/api/v1/info", InfoEndpoints.GetInfo)
            .WithName("GetInfo")
            .WithTags("Info")
            .AllowAnonymous();
    }
}
