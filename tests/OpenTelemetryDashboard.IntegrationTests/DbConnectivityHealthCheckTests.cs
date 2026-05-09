using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies that <c>/healthz</c> probes the database. With a reachable DB
/// the aggregate is <c>Healthy</c>; pointing the connection string at a
/// non-existent path / unreachable file makes the probe fail and the
/// endpoint return 503 with body <c>Unhealthy</c>.
/// </summary>
public sealed class DbConnectivityHealthCheckTests
{
    [Fact]
    public async Task Reachable_Db_Reports_Healthy()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(dbPath);
            await EnsureSchemaAsync(factory);
            using var client = factory.CreateClient();

            using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldBe("Healthy");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Unreachable_Db_Reports_Unhealthy_503()
    {
        // Boot with a working DB so migrations + seeders succeed, then
        // remove the file underneath. SQLite opens a fresh connection per
        // health check (the DbContext factory pattern), so the new connect
        // fails and the probe reports Unhealthy. Pool clearing is needed
        // because Microsoft.Data.Sqlite caches handles per connection
        // string — a stale one would still resolve.
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(dbPath);
            await EnsureSchemaAsync(factory);

            using var client = factory.CreateClient();

            // Sanity check: healthy before we yank the DB.
            using (var preResp = await client.GetAsync(new Uri("/healthz", UriKind.Relative)))
            {
                preResp.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(dbPath);

            using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

            // Default HealthCheckOptions maps Unhealthy → 503.
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldBe("Unhealthy");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    private static WebApplicationFactory<Program> BuildHost(string dbPath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dashboard:Storage:Provider"] = "Sqlite",
                    ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                    // Set both tokens so the auth-posture check stays Healthy
                    // and the aggregate /healthz reflects the DB probe alone.
                    ["Dashboard:BrowserToken"] = "browser-secret",
                    ["Dashboard:Otlp:ApiKey"] = "otlp-secret",
                });
            });
        });

    private static async Task EnsureSchemaAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"oteldash-dbhealth-{Guid.NewGuid():N}.db");
}
