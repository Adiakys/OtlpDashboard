using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace OpenTelemetryDashboard.Persistence.PostgreSql;

public static class PostgreSqlTelemetryStoreExtensions
{
    /// <summary>
    /// Registers a pooled <see cref="TelemetryDbContext"/> factory backed by PostgreSQL.
    /// Migrations for this provider live in this assembly.
    /// </summary>
    public static IServiceCollection AddPostgreSqlTelemetryStore(
        this IServiceCollection services,
        string connectionString,
        int poolSize = 128)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddTelemetryPersistenceCore(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(PostgreSqlTelemetryStoreExtensions).Assembly.GetName().Name);
            });
            options.ReplaceService<IModelCustomizer, PostgresJsonAttributeFunctionCustomizer>();
        }, poolSize);
    }

    /// <summary>
    /// Variant that resolves the PostgreSQL connection string lazily from the
    /// service provider — use this when the value lives behind
    /// <c>IConfiguration</c> and may be overridden after
    /// <c>WebApplication.CreateBuilder</c> (e.g. integration tests).
    /// </summary>
    public static IServiceCollection AddPostgreSqlTelemetryStore(
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

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(PostgreSqlTelemetryStoreExtensions).Assembly.GetName().Name);
            });
            options.ReplaceService<IModelCustomizer, PostgresJsonAttributeFunctionCustomizer>();
        }, poolSize);
    }
}
