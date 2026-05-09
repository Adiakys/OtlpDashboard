using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Dashboards.Storage;
using OpenTelemetryDashboard.Persistence.Demo;
using OpenTelemetryDashboard.Persistence.HealthChecks;
using OpenTelemetryDashboard.Persistence.Ingestion;
using OpenTelemetryDashboard.Persistence.Readers;
using OpenTelemetryDashboard.Persistence.Retention;
using OpenTelemetryDashboard.Persistence.Sinks;
using OpenTelemetryDashboard.Persistence.Stores;

namespace OpenTelemetryDashboard.Persistence;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the provider-agnostic <see cref="TelemetryDbContext"/> factory,
    /// the EF Core-backed <see cref="ITraceSink"/> / <see cref="ILogSink"/>,
    /// the corresponding <see cref="ITraceReader"/> / <see cref="ILogReader"/>,
    /// and the shared <see cref="ResourceCache"/>.
    /// Callers supply a provider-specific <paramref name="configureProvider"/>
    /// delegate (see e.g. AddSqliteTelemetryStore in
    /// OpenTelemetryDashboard.Persistence.Sqlite).
    /// </summary>
    public static IServiceCollection AddTelemetryPersistenceCore(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider,
        int poolSize = 128,
        int resourceCacheSize = 1_024)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddPooledDbContextFactory<TelemetryDbContext>(configureProvider, poolSize);
        return RegisterSharedPersistenceServices(services, resourceCacheSize);
    }

    public static IServiceCollection AddTelemetryPersistenceCore(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> configureProvider,
        int poolSize = 128,
        int resourceCacheSize = 1_024)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddPooledDbContextFactory<TelemetryDbContext>(configureProvider, poolSize);
        return RegisterSharedPersistenceServices(services, resourceCacheSize);
    }

    private static IServiceCollection RegisterSharedPersistenceServices(
        IServiceCollection services,
        int resourceCacheSize)
    {
        services.AddSingleton(_ => new ResourceCache(resourceCacheSize));
        services.AddSingleton(_ => new InstrumentCache());

        // Sink-side counters drive both per-batch logs ("dropped X spans") and
        // the TelemetrySinkHealthCheck. Single shared instance across signals.
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TelemetrySinkMetrics>();

        // Service-map dependency-synthesis keys. Bound here (not in
        // the host) because the service-map reader is the only
        // consumer — keeps the persistence module self-contained.
        // Defaults baked into ServiceMapOptions; appsettings only
        // needs to override.
        services.AddOptions<ServiceMapOptions>()
            .BindConfiguration(ServiceMapOptions.SectionName);

        services.AddSingleton<ITraceSink, EfCoreTraceSink>();
        services.AddSingleton<ILogSink, EfCoreLogSink>();
        services.AddSingleton<IMetricSink, EfCoreMetricSink>();

        services.AddSingleton<ITraceReader, EfCoreTraceReader>();
        services.AddSingleton<ILogReader, EfCoreLogReader>();
        services.AddSingleton<IMetricReader, EfCoreMetricReader>();
        services.AddSingleton<IServiceMapReader, EfCoreServiceMapReader>();

        services.AddSingleton<IDashboardStore, EfCoreDashboardStore>();
        services.AddSingleton<IWidgetDefinitionStore, EfCoreWidgetDefinitionStore>();

        services.AddDemoHistoricalDataSeeder();
        
        return services;
    }

    /// <summary>
    /// Registers the background <see cref="TelemetryWriter"/> that dispatches
    /// telemetry batches to the sinks registered via
    /// <see cref="AddTelemetryPersistenceCore"/>.
    /// </summary>
    public static IServiceCollection AddTelemetryWriter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<TelemetryWriter>();
        return services;
    }

    /// <summary>
    /// Registers the retention options, the EF-core backed log/trace/metric
    /// policies, and the background host that enforces them.
    /// </summary>
    public static IServiceCollection AddTelemetryRetention(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TelemetryLimitsOptions>()
            .Bind(configuration.GetSection(TelemetryLimitsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ILogRetentionPolicy, EfCoreLogRetentionPolicy>();
        services.TryAddSingleton<ITraceRetentionPolicy, EfCoreTraceRetentionPolicy>();
        services.TryAddSingleton<IMetricRetentionPolicy, EfCoreMetricRetentionPolicy>();

        services.AddHostedService<TelemetryRetentionHost>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="TelemetrySinkHealthCheck"/> against the standard
    /// ASP.NET Core health-checks pipeline so <c>/healthz</c> degrades when
    /// any signal sink has dropped a batch in the recent window.
    /// </summary>
    public static IHealthChecksBuilder AddTelemetrySinkHealthCheck(
        this IHealthChecksBuilder builder,
        string name = "telemetry-sink",
        HealthStatus failureStatus = HealthStatus.Degraded)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<TelemetrySinkHealthCheck>(name, failureStatus);
    }
}
