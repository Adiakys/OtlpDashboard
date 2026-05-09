using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies the fail-closed-in-Production posture: missing tokens with no
/// explicit AllowAnonymous opt-in must refuse to start; an opt-in boots the
/// host but flags <c>/healthz</c> as Degraded; tokens-set Production boots
/// healthy.
/// </summary>
public sealed class AuthPostureTests
{
    [Fact]
    public void Production_With_Empty_Tokens_Refuses_To_Start()
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
                        // No tokens, no AllowAnonymous: must throw.
                    });
                });
            });

            // Eager DI resolution doesn't trigger the validator (which lives
            // in UseDashboardPipeline). We need to start the host — that's
            // what CreateClient does indirectly. The validator throws inside
            // `Configure` callbacks, which surfaces as InvalidOperationException
            // out of the WebApplicationFactory.
            var ex = Should.Throw<InvalidOperationException>(() => _ = factory.CreateClient());
            ex.Message.ShouldContain("Auth is required");
            ex.Message.ShouldContain("Dashboard:BrowserToken");
            ex.Message.ShouldContain("Dashboard:Otlp:ApiKey");
            ex.Message.ShouldContain("AllowAnonymous");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Production_With_AllowAnonymous_Opt_In_Boots_But_Healthz_Is_Degraded()
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
                        ["Dashboard:Auth:AllowAnonymous"] = "true",
                    });
                });
            });

            // Migrate the schema for /healthz's underlying queries.
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
                await using var ctx = await dbFactory.CreateDbContextAsync();
                await ctx.Database.MigrateAsync();
            }

            using var client = factory.CreateClient();

            using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

            // ASP.NET's default HealthCheckOptions returns 200 for both
            // Healthy and Degraded (only Unhealthy maps to 503), so we
            // read the textual aggregate status from the body.
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
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

    private static WebApplicationFactory<Program> BuildHost(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(configure);

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"oteldash-authposture-{Guid.NewGuid():N}.db");
}
