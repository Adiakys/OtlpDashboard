using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies the CORS posture: empty AllowedOrigins (default) issues no
/// <c>Access-Control-Allow-Origin</c> header, configured origins do —
/// and only for an exact match.
/// </summary>
public sealed class CorsTests
{
    [Fact]
    public async Task Cross_origin_request_gets_no_allow_origin_header_by_default()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(dbPath, allowedOrigins: null);
            await MigrateAsync(factory);
            using var client = factory.CreateClient();

            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("/api/v1/info", UriKind.Relative));
            request.Headers.Add("Origin", "https://attacker.example.com");

            using var response = await client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            // No header at all — browser will reject the response from JS.
            response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Configured_origin_gets_explicit_allow_origin_header()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(
                dbPath,
                allowedOrigins: new[] { "https://dashboard.example.com" });
            await MigrateAsync(factory);
            using var client = factory.CreateClient();

            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("/api/v1/info", UriKind.Relative));
            request.Headers.Add("Origin", "https://dashboard.example.com");

            using var response = await client.SendAsync(request);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.GetValues("Access-Control-Allow-Origin")
                .ShouldContain("https://dashboard.example.com");
            // Cookie attachment requires AllowCredentials() — the policy
            // sets it on so the HttpOnly auth cookie can ride a cross-origin
            // request from a configured SPA host.
            response.Headers.GetValues("Access-Control-Allow-Credentials")
                .ShouldContain("true");
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Origin_not_in_allow_list_gets_no_allow_origin_header()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(
                dbPath,
                allowedOrigins: new[] { "https://dashboard.example.com" });
            await MigrateAsync(factory);
            using var client = factory.CreateClient();

            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri("/api/v1/info", UriKind.Relative));
            request.Headers.Add("Origin", "https://attacker.example.com");

            using var response = await client.SendAsync(request);

            // Server still answers (browsers do the gating); just no CORS
            // header so the browser refuses to expose the body to JS.
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    private static WebApplicationFactory<Program> BuildHost(string dbPath, string[]? allowedOrigins)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Dashboard:Storage:Provider"] = "Sqlite",
                    ["ConnectionStrings:Sqlite"] = $"Data Source={dbPath}",
                };
                if (allowedOrigins is not null)
                {
                    for (var i = 0; i < allowedOrigins.Length; i++)
                    {
                        settings[$"Dashboard:Cors:AllowedOrigins:{i}"] = allowedOrigins[i];
                    }
                }
                config.AddInMemoryCollection(settings);
            });
        });
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ctxFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await ctxFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"oteldash-cors-{Guid.NewGuid():N}.db");
}
