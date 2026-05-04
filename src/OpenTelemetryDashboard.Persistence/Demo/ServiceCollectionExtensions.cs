using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenTelemetryDashboard.Persistence.Demo;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the demo historical-data seeder and its options.
    /// The seeder is a transient service the host calls explicitly after
    /// migrations; it is a no-op unless <see cref="DemoSeedOptions.Enabled"/>
    /// is true. Always-registered so the host can resolve and call it
    /// without conditional DI wiring.
    /// </summary>
    public static IServiceCollection AddDemoHistoricalDataSeeder(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        
        services.AddTransient(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var options = new DemoSeedOptions();
            configuration.Bind(DemoSeedOptions.SectionName, options);
            return Options.Create(options);
        });
        
        services.AddTransient<HistoricalDataSeeder>();
        return services;
    }

    /// <summary>
    /// Demo historical-data seeder — backfills 7 days of plausible traces
    /// and logs when `Dashboard:DemoSeed:Enabled` is true. Internally
    /// skipped if the spans table already has rows, so re-runs on a
    /// populated DB are a no-op. Wrapped in try/catch so a seed error
    /// never blocks startup of the real ingestion pipeline.
    /// </summary>
    public static async Task SeedDemoHistoryDataAsync(this AsyncServiceScope scope, ILogger logger)
    {
        try
        {
            var demoSeeder = scope.ServiceProvider
                .GetRequiredService<HistoricalDataSeeder>();
            await demoSeeder.SeedAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.DemoSeedingFailed(ex);
        }
    }
}