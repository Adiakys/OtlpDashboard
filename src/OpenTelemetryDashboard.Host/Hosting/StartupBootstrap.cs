using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Dashboards.Seeding;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Demo;
using OpenTelemetryDashboard.Persistence.Locking;

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
        // Distributed lock around MigrateAsync. On a rolling deploy multiple
        // replicas race to apply the same DDL; without coordination two
        // pods can deadlock or partially apply. The lock implementation is
        // provider-specific (Postgres advisory lock, SQL Server sp_getapplock,
        // SQLite no-op) — see IMigrationLock. Holders dispose to release.
        // SQLite migrator is idempotent and we run a single writer process,
        // so the cost on an up-to-date schema is a single metadata query;
        // the lock is essentially free in that case.
        var migrationLock = services.GetRequiredService<IMigrationLock>();
        await using (await migrationLock.AcquireAsync(CancellationToken.None).ConfigureAwait(false))
        {
            var factory = services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            await context.Database.MigrateAsync();
        }
    }

    private static async Task SeedBuiltinDashboardsAsync(IServiceProvider services)
    {
        // Idempotent: an id already in the store is skipped silently.
        var seeder = services.GetRequiredService<IBuiltinDashboardSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
}
