using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies the token-presence posture: auth is opt-in per surface in EVERY
/// environment. An unset token leaves that surface public (allow-all) and
/// flags <c>/healthz</c> as Degraded — there is no fail-closed boot gate.
/// Tokens-set Production boots healthy.
/// </summary>
public sealed class AuthPostureTests
{
    [Fact]
    public async Task Production_With_Empty_Tokens_Boots_Public_And_Healthz_Is_Degraded()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                        // No tokens: the surface is public, in Production too.
                    });
                });
            });

            // Migrate the schema for the read API + /healthz queries.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
                await using var ctx = await dbFactory.CreateDbContextAsync();
                await ctx.Database.MigrateAsync();
            }

            using var client = factory.CreateClient();

            // Empty BrowserToken → read API is allow-all even in Production.
            using var apiResponse = await client.GetAsync(new Uri("/api/v1/dashboards", UriKind.Relative));
            apiResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

            // ASP.NET's default HealthCheckOptions returns 200 for both
            // Healthy and Degraded (only Unhealthy maps to 503), so we
            // read the textual aggregate status from the body.
            using var healthResponse = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
            healthResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await healthResponse.Content.ReadAsStringAsync();
            body.ShouldBe("Degraded");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Production_With_Tokens_Configured_Boots_And_Healthz_Is_Healthy()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                        ["Dashboard:BrowserToken"] = "browser-secret",
                        ["Dashboard:Otlp:ApiKey"] = "otlp-secret",
                    });
                });
            });

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
                await using var ctx = await dbFactory.CreateDbContextAsync();
                await ctx.Database.MigrateAsync();
            }

            using var client = factory.CreateClient();

            using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Development_With_Empty_Tokens_Boots_And_Allows_Anonymous_Access()
    {
        // Backwards-compat: TestHostFixture (and existing test suites) rely
        // on Development + empty tokens being permissive. Locking that down
        // would require updating every fixture and ruin the local-dev UX.
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                    });
                });
            });

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
                await using var ctx = await dbFactory.CreateDbContextAsync();
                await ctx.Database.MigrateAsync();
            }

            using var client = factory.CreateClient();

            // No Authorization header — Development allow-all lets it through.
            using var response = await client.GetAsync(new Uri("/api/v1/dashboards", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Info_RequireAuth_Is_False_When_BrowserToken_Empty()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                    });
                });
            });
            await MigrateAsync(factory);

            using var client = factory.CreateClient();
            using var response = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("\"requireAuth\":false");
            // Auth disabled → the surface is public, so /info is not redacted.
            body.ShouldNotContain("\"version\":null");
            body.ShouldContain("\"storageProvider\":\"Sqlite\"");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Info_RequireAuth_Is_True_For_Anonymous_When_BrowserToken_Set()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                        ["Dashboard:BrowserToken"] = "browser-secret",
                        ["Dashboard:Otlp:ApiKey"] = "otlp-secret",
                    });
                });
            });
            await MigrateAsync(factory);

            using var client = factory.CreateClient();
            using var response = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldContain("\"requireAuth\":true");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    private static WebApplicationFactory<Program> BuildHost(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(configure);

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"oteldash-authposture-{Guid.NewGuid():N}.db");
}
