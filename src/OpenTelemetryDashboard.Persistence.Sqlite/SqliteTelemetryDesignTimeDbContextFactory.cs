using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTelemetryDashboard.Persistence.Sqlite;

public sealed class SqliteTelemetryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    private const string DefaultDesignTimeConnectionString = "Data Source=./telemetry.design.db";
    private const string ConnectionStringEnvironmentVariable = "OTELDASHBOARD_SQLITE_DESIGNTIME";

    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                               ?? DefaultDesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlite(connectionString, sqlite =>
            {
                sqlite.MigrationsAssembly(typeof(SqliteTelemetryDesignTimeDbContextFactory).Assembly.GetName().Name);
            })
            .Options;

        return new TelemetryDbContext(options);
    }
}
