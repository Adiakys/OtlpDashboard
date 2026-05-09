using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies the retention posture: shipping defaults are bounded, but an
/// operator who explicitly sets any retention window to <c>0</c> (records
/// kept indefinitely) sees <c>/healthz</c> flip to <c>Degraded</c>.
/// </summary>
public sealed class RetentionPostureTests
{
    [Fact]
    public async Task Shipping_Defaults_Boot_Healthy()
    {
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
                        // Configure both auth tokens so the auth-posture check
                        // doesn't shadow the retention-posture check we're
                        // exercising; the aggregate /healthz reflects every
                        // registered probe.
                        ["Dashboard:BrowserToken"] = "browser-secret",
                        ["Dashboard:Otlp:ApiKey"] = "otlp-secret",
                        // No TelemetryLimits override: defaults apply (14/7/30).
                    });
                });
            });

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

    [Theory]
    [InlineData("Dashboard:TelemetryLimits:MaxLogDays")]
    [InlineData("Dashboard:TelemetryLimits:MaxTraceDays")]
    [InlineData("Dashboard:TelemetryLimits:MaxMetricDays")]
    public async Task Disabling_Any_Retention_Window_Degrades_Healthz(string disabledKey)
    {
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
                        [disabledKey] = "0",
                    });
                });
            });

            await EnsureSchemaAsync(factory);
            using var client = factory.CreateClient();

            using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

            // ASP.NET's default HealthCheckOptions returns 200 for both
            // Healthy and Degraded; the textual aggregate sits in the body.
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync();
            body.ShouldBe("Degraded");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    private static async Task EnsureSchemaAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    private static WebApplicationFactory<Program> BuildHost(Action<IWebHostBuilder> configure) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(configure);

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"oteldash-retention-{Guid.NewGuid():N}.db");
}
