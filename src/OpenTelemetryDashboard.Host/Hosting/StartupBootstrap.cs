using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Dashboards.Seeding;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Demo;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Post-Build, pre-pipeline bootstrap: applies EF Core migrations, seeds
/// built-in dashboards, and (when enabled) generates demo data. All steps
/// are idempotent — safe to run on every boot.
/// </summary>
internal static class StartupBootstrap
{
    public static async Task RunStartupAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        await ApplyMigrationsAsync(scope.ServiceProvider);
        await SeedBuiltinDashboardsAsync(scope.ServiceProvider);
        await scope.SeedDemoHistoryDataAsync(app.Logger);
    }

    private static async Task ApplyMigrationsAsync(IServiceProvider services)
    {
        // SQLite provider's migrator is idempotent and we run a single writer
        // process, so the cost on an up-to-date schema is a single metadata
        // query. Production containers rely on this to create the schema on
        // first run.
        var factory = services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    private static async Task SeedBuiltinDashboardsAsync(IServiceProvider services)
    {
        // Idempotent: an id already in the store is skipped silently.
        var seeder = services.GetRequiredService<IBuiltinDashboardSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
}
