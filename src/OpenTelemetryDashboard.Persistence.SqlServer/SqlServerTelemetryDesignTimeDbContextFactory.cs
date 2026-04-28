using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OpenTelemetryDashboard.Persistence.SqlServer;

public sealed class SqlServerTelemetryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<TelemetryDbContext>
{
    private const string DefaultDesignTimeConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=oteldashboard_design;Trusted_Connection=True;TrustServerCertificate=True";
    private const string ConnectionStringEnvironmentVariable = "OTELDASHBOARD_SQLSERVER_DESIGNTIME";

    public TelemetryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
                               ?? DefaultDesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlServer(connectionString, sqlServer =>
            {
                sqlServer.MigrationsAssembly(typeof(SqlServerTelemetryDesignTimeDbContextFactory).Assembly.GetName().Name);
            })
            .Options;

        return new TelemetryDbContext(options);
    }
}
