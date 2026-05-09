using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence.Locking;

namespace OpenTelemetryDashboard.Persistence.SqlServer;

public static class SqlServerTelemetryStoreExtensions
{
    /// <summary>
    /// Registers a pooled <see cref="TelemetryDbContext"/> factory backed by SQL Server.
    /// Migrations for this provider live in this assembly.
    /// </summary>
    public static IServiceCollection AddSqlServerTelemetryStore(
        this IServiceCollection services,
        string connectionString,
        int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<IMigrationLock>(_ => new SqlServerMigrationLock(connectionString));
        return services.AddTelemetryPersistenceCore(options =>
        {
            options.UseSqlServer(connectionString, sqlServer =>
            {
                sqlServer.MigrationsAssembly(typeof(SqlServerTelemetryStoreExtensions).Assembly.GetName().Name);
            });
            options.ReplaceService<IModelCustomizer, SqlServerJsonAttributeFunctionCustomizer>();
        }, poolSize);
    }

    /// <summary>
    /// Variant that resolves the SQL Server connection string lazily from the
    /// service provider — use this when the value lives behind
    /// <c>IConfiguration</c> and may be overridden after
    /// <c>WebApplication.CreateBuilder</c> (e.g. integration tests).
    /// </summary>
    public static IServiceCollection AddSqlServerTelemetryStore(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory,
        int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionStringFactory);

        services.AddSingleton<IMigrationLock>(sp => new SqlServerMigrationLock(connectionStringFactory(sp)));
        return services.AddTelemetryPersistenceCore((sp, options) =>
        {
            var connectionString = connectionStringFactory(sp);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            options.UseSqlServer(connectionString, sqlServer =>
            {
                sqlServer.MigrationsAssembly(typeof(SqlServerTelemetryStoreExtensions).Assembly.GetName().Name);
            });
            options.ReplaceService<IModelCustomizer, SqlServerJsonAttributeFunctionCustomizer>();
        }, poolSize);
    }
}
