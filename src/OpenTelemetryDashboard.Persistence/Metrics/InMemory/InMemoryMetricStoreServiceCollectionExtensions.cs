using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Retention;

namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

public static class InMemoryMetricStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory implementation of <see cref="IMetricSink"/> and
    /// <see cref="IMetricReader"/>. Both share a singleton
    /// <see cref="InMemoryMetricStorage"/> so writes are immediately visible to
    /// reads. Data is transient: lost on process restart (mirror of the
    /// Aspire dashboard behaviour).
    /// </summary>
    public static IServiceCollection AddInMemoryMetricStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<InMemoryMetricStoreOptions>()
            .Bind(configuration.GetSection(InMemoryMetricStoreOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<InMemoryMetricStorage>();
        services.AddSingleton<IMetricSink, InMemoryMetricSink>();
        services.AddSingleton<IMetricReader, InMemoryMetricReader>();
        services.AddSingleton<IMetricRetentionPolicy, InMemoryMetricRetentionPolicy>();

        return services;
    }
}
