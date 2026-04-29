using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ingestion-pipeline primitives that live in the domain:
    /// the bounded <see cref="TelemetryChannel"/> and related option binders.
    /// Does NOT register sinks, readers, or storage — those belong to
    /// infrastructure modules (e.g. <c>OpenTelemetryDashboard.Persistence</c>).
    /// </summary>
    public static IServiceCollection AddTelemetryCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TelemetryChannelOptions>()
            .Bind(configuration.GetSection(TelemetryChannelOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services
            .AddOptions<IngestionShutdownOptions>()
            .Bind(configuration.GetSection(IngestionShutdownOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TelemetryChannel>();

        return services;
    }
}
