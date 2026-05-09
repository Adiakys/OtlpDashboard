using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies that the four rate-limit policies registered in <c>Program.cs</c>
/// actually fire 429 when exceeded. The fixture lowers each bucket aggressively
/// so the test doesn't have to send hundreds of requests to trip the default
/// production limits.
/// </summary>
public sealed class RateLimitTests : IAsyncLifetime
{
    private const string BrowserToken = "browser-rl-token";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"oteldash-rl-{Guid.NewGuid():N}.db");

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={_dbPath}",
                        ["Dashboard:BrowserToken"] = BrowserToken,
                        // Aggressive limits so the tests trip in O(5) requests
                        // instead of O(thousands).
                        ["Dashboard:RateLimits:ReadApi:PermitsPerSecond"] = "2",
                        ["Dashboard:RateLimits:ReadApi:Burst"] = "0",
                        ["Dashboard:RateLimits:Mutations:PermitsPerSecond"] = "1",
                        ["Dashboard:RateLimits:Mutations:Burst"] = "0",
                        ["Dashboard:RateLimits:PackInstall:MaxConcurrent"] = "1",
                        ["Dashboard:RateLimits:PackInstall:ConcurrencyQueueLimit"] = "0",
                    });
                });
            });

        _ = _factory.Services;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        TempSqliteFiles.TryDelete(_dbPath);
    }

    [Fact]
    public async Task ReadApi_ReturnsTooManyRequests_AfterBurstExceeded()
    {
        var client = CreateClient();

        // PermitsPerSecond=2, Burst=0, sliding 1s window with 4 segments. Fire
        // 6 back-to-back: at least one must come back as 429. We don't insist
        // on the exact firing index because the sliding window's segment
        // boundaries depend on wall-clock alignment.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            using var resp = await client.GetAsync("/api/v1/dashboards");
            statuses.Add(resp.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Mutations_ReturnsTooManyRequests_AfterBurstExceeded()
    {
        var client = CreateClient();

        // PermitsPerSecond=1, Burst=0. The bodies fail validation (we just
        // need the rate-limit middleware to fire before the handler), so the
        // first calls return 4xx and a later one returns 429.
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var resp = await client.PostAsJsonAsync("/api/v1/dashboards", new
            {
                id = $"rl-{i}",
                name = $"rl-{i}",
                widgets = Array.Empty<object>(),
            });
            statuses.Add(resp.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task PackInstall_ReturnsTooManyRequests_WhenConcurrentReloadsExceedLimit()
    {
        var client = CreateClient();

        // MaxConcurrent=1, queue=0. Two concurrent reload calls: one wins the
        // single slot, the other 429s. Reload itself is fast so we fire 6 in
        // parallel to maximise the chance of stepping on the same in-flight
        // permit even when the first one returns quickly.
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => client.PostAsync("/api/v1/packs/reload", content: null))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        try
        {
            var statuses = responses.Select(r => r.StatusCode).ToArray();
            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        }
        finally
        {
            foreach (var r in responses)
            {
                r.Dispose();
            }
        }
    }

    private HttpClient CreateClient()
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BrowserToken);
        return client;
    }
}
