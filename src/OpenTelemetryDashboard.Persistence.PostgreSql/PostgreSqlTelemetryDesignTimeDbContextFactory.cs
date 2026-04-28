using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTelemetryDashboard.Persistence.PostgreSql;

public sealed class PostgreSqlTelemetryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Host=localhost;Port=5432;Database=oteldashboard_design;Username=postgres;Password=postgres";
    private const string ConnectionStringEnvironmentVariable = "OTELDASHBOARD_POSTGRESQL_DESIGNTIME";

    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                               ?? DefaultDesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(PostgreSqlTelemetryDesignTimeDbContextFactory).Assembly.GetName().Name);
            })
            .Options;

        return new TelemetryDbContext(options);
    }
}
