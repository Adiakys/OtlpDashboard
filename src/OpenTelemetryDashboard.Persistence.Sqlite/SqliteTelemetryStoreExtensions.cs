using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace OpenTelemetryDashboard.Persistence.Sqlite;

public static class SqliteTelemetryStoreExtensions
{
    /// <summary>
    /// Registers a pooled <see cref="TelemetryDbContext"/> factory backed by SQLite.
    /// Migrations for this provider live in this assembly.
    /// </summary>
    public static IServiceCollection AddSqliteTelemetryStore(
        this IServiceCollection services,
        string connectionString,
        int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddTelemetryPersistenceCore(options =>
        {
            options.UseSqlite(connectionString, sqlite =>
            {
                sqlite.MigrationsAssembly(typeof(SqliteTelemetryStoreExtensions).Assembly.GetName().Name);
                // Default for the SQLite provider is 1-2 rows per IDbCommand
                // (EF emits one INSERT per row). With self-instrumentation
                // enabled the EF Core SDK creates one activity per command,
                // so a batch of 100 metric points would surface as ~100
                // "INSERT MetricPoints" spans under TelemetryWriter.Dispatch.
                // Bumping the cap collapses them into a handful of multi-row
                // INSERTs (SQLite supports VALUES(..),(..),(..) up to ~32766
                // parameters per statement, so 100 × ~10 cols stays safely
                // under the limit) and keeps trace listings readable.
                sqlite.MaxBatchSize(100);
            });
            options.ReplaceService<IModelCustomizer, SqliteJsonAttributeFunctionCustomizer>();
            options.AddInterceptors(new SqlitePragmaInterceptor());
        }, poolSize);
    }

    /// <summary>
    /// Variant that resolves the SQLite connection string lazily from the
    /// service provider — use this when the value lives behind
    /// <c>IOptions&lt;T&gt;</c> and may be overridden after
    /// <c>WebApplication.CreateBuilder</c> (e.g. integration tests).
    /// </summary>
    public static IServiceCollection AddSqliteTelemetryStore(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        return services.AddTelemetryPersistenceCore((sp, options) =>
        {
            var connectionString = connectionStringFactory(sp);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            options.UseSqlite(connectionString, sqlite =>
            {
                sqlite.MigrationsAssembly(typeof(SqliteTelemetryStoreExtensions).Assembly.GetName().Name);
                // Default for the SQLite provider is 1-2 rows per IDbCommand
                // (EF emits one INSERT per row). With self-instrumentation
                // enabled the EF Core SDK creates one activity per command,
                // so a batch of 100 metric points would surface as ~100
                // "INSERT MetricPoints" spans under TelemetryWriter.Dispatch.
                // Bumping the cap collapses them into a handful of multi-row
                // INSERTs (SQLite supports VALUES(..),(..),(..) up to ~32766
                // parameters per statement, so 100 × ~10 cols stays safely
                // under the limit) and keeps trace listings readable.
                sqlite.MaxBatchSize(100);
            });
            options.ReplaceService<IModelCustomizer, SqliteJsonAttributeFunctionCustomizer>();
            options.AddInterceptors(new SqlitePragmaInterceptor());
        }, poolSize);
    }
}
