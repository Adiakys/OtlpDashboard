using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Ingestion.Grpc;
using OpenTelemetryDashboard.Ingestion.Http;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOtlpIngestion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<OtlpTraceTranslator>();
        services.AddSingleton<OtlpLogTranslator>();
        services.AddSingleton<OtlpMetricTranslator>();

        services.AddSingleton<OtlpTraceService>();
        services.AddSingleton<OtlpLogsService>();
        services.AddSingleton<OtlpMetricsService>();

        return services;
    }

    /// <summary>
    /// Maps the three OTLP gRPC services (traces/logs/metrics). The optional
    /// <paramref name="configure"/> callback runs against each service's
    /// convention builder, letting callers apply cross-cutting conventions
    /// (e.g. <c>.RequireAuthorization(...)</c>) uniformly.
    /// </summary>
    public static IEndpointRouteBuilder MapOtlpGrpcServices(
        this IEndpointRouteBuilder endpoints,
        Action<IEndpointConventionBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var trace = endpoints.MapGrpcService<OtlpTraceService>();
        var logs = endpoints.MapGrpcService<OtlpLogsService>();
        var metrics = endpoints.MapGrpcService<OtlpMetricsService>();

        if (configure is not null)
        {
            configure(trace);
            configure(logs);
            configure(metrics);
        }

        return endpoints;
    }
}
