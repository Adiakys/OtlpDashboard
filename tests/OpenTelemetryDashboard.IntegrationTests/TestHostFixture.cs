using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Boots the <see cref="Program"/> host with an isolated SQLite file under the
/// system temp directory. Migrations run once per fixture instance.
/// </summary>
public sealed class TestHostFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabasePath { get; } =
        Path.Combine(Path.GetTempPath(), $"oteldash-test-{Guid.NewGuid():N}.db");

    public string ConnectionString => $"Data Source={DatabasePath}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dashboard:Storage:Provider"] = "Sqlite",
                ["ConnectionStrings:Sqlite"] = ConnectionString,
                ["Dashboard:Ingestion:Channel:Capacity"] = "1000",
                ["Dashboard:Ingestion:Channel:MaxBatchSize"] = "64",
                ["Dashboard:Ingestion:Channel:FlushIntervalMs"] = "50",
            });
        });
    }

    public async Task InitializeAsync()
    {
        // Force the host to start so migrations run.
        _ = Services;

        await using var scope = Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        TempSqliteFiles.TryDelete(DatabasePath);
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();
}
