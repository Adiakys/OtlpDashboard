using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Host.Configuration;

public sealed class IngestionServerOptions
{
    public const string SectionName = "Dashboard:Ingestion";

    public GrpcServerOptions Grpc { get; set; } = new();
    public HttpServerOptions Http { get; set; } = new();
    public IngestionShutdownOptions Shutdown { get; set; } = new();
}

public sealed class GrpcServerOptions
{
    [Range(1, 65_535)]
    public int Port { get; set; } = 4317;

    [Range(1_024, 1024 * 1024 * 1024)]
    public int MaxReceiveMessageSize { get; set; } = 16 * 1024 * 1024;
}

public sealed class HttpServerOptions
{
    [Range(1, 65_535)]
    public int Port { get; set; } = 4318;

    [Range(1_024, long.MaxValue)]
    public long MaxRequestBodySize { get; set; } = 16 * 1024 * 1024;

    public HttpRateLimitOptions RateLimit { get; set; } = new();
}

public sealed class HttpRateLimitOptions
{
    [Range(1, 1_000_000)]
    public int PermitsPerSecond { get; set; } = 200;

    [Range(0, 1_000_000)]
    public int Burst { get; set; } = 500;
}

public static class IngestionServerOptionsExtensions
{
    public static IServiceCollection AddIngestionServerOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<IngestionServerOptions>()
            .Bind(configuration.GetSection(IngestionServerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
