using Microsoft.EntityFrameworkCore;
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
            });
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
            });
        }, poolSize);
    }
}
