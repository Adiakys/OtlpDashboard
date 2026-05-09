using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies that any exception escaping an endpoint is reshaped into the same
/// RFC 7807 ProblemDetails envelope that validation/concurrency failures
/// already produce, and that the global hardening headers ride on the error
/// response too.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task Unhandled_exception_returns_problem_details_500()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(dbPath, services =>
            {
                services.RemoveAll<IDashboardStore>();
                services.AddSingleton<IDashboardStore, ThrowingDashboardStore>();
            });

            await MigrateAsync(factory);

            using var client = factory.CreateClient();
            using var response = await client.GetAsync(new Uri("/api/v1/dashboards", UriKind.Relative));

            response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            root.GetProperty("status").GetInt32().ShouldBe(500);
            root.GetProperty("title").GetString().ShouldNotBeNullOrEmpty();
            root.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();

            // Security headers must survive Response.Clear() inside the
            // exception handler — they're queued via OnStarting.
            response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
            response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
            response.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    [Fact]
    public async Task Development_includes_exception_type_for_diagnostics()
    {
        var dbPath = TempDbPath();
        try
        {
            using var factory = BuildHost(dbPath, services =>
            {
                services.RemoveAll<IDashboardStore>();
                services.AddSingleton<IDashboardStore, ThrowingDashboardStore>();
            });

            await MigrateAsync(factory);

            using var client = factory.CreateClient();
            using var response = await client.GetAsync(new Uri("/api/v1/dashboards", UriKind.Relative));

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            root.TryGetProperty("exceptionType", out var typeProp).ShouldBeTrue();
            typeProp.GetString()!.ShouldContain(nameof(InvalidOperationException));
        }
        finally
        {
            TempSqliteFiles.TryDelete(dbPath);
        }
    }

    private static WebApplicationFactory<Program> BuildHost(
        string dbPath,
        Action<IServiceCollection> configureServices) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
            builder.ConfigureTestServices(configureServices);
        });

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ctxFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await ctxFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"oteldash-exhandler-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Store that throws only on the path the test exercises
    /// (<see cref="GetAllAsync"/>, called by GET /api/v1/dashboards). The
    /// other methods are no-ops so the host's built-in dashboard seeder
    /// (which calls <see cref="GetAllIdsAsync"/> at startup) can boot.
    /// </summary>
    private sealed class ThrowingDashboardStore : IDashboardStore
    {
        public Task<IReadOnlyList<Dashboard>> GetAllAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("simulated downstream failure");

        public Task<IReadOnlyList<Guid>> GetAllIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Dashboard?>(null);

        public Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
