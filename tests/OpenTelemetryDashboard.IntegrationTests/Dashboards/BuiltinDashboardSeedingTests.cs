using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests.Dashboards;

/// <summary>
/// Drives <c>BuiltinDashboardSeeder</c> through a real host. The fixture
/// pre-populates a temp packs root with a pack containing built-in
/// dashboards before the host boots, so the seeder runs against the
/// same configured path the host reads.
/// </summary>
public sealed class BuiltinDashboardSeedingTests : IClassFixture<BuiltinDashboardSeedingTests.SeedingTestHost>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SeedingTestHost _host;

    public BuiltinDashboardSeedingTests(SeedingTestHost host) => _host = host;

    [Fact]
    public async Task Default_File_Becomes_Default_Dashboard_On_Fresh_Db()
    {
        using var client = _host.CreateClient();
        var dto = await client.GetFromJsonAsync<DashboardDto>(
            new Uri($"/api/v1/dashboards/{Dashboard.DefaultId}", UriKind.Relative), JsonOptions);

        dto.ShouldNotBeNull();
        dto!.Id.ShouldBe(Dashboard.DefaultId);
        dto.Name.ShouldBe("Welcome from seed");
        dto.Widgets.Count.ShouldBe(1);
        dto.Widgets[0].Kind.ShouldBe("std:text");
    }

    [Fact]
    public async Task DashId_Default_Maps_To_DefaultId()
    {
        // The fixture's pack declares a dashboard with id "default" plus
        // an explicit Guid matching `Dashboard.DefaultId`. This pins the
        // outcome through the API contract: GET on the well-known id
        // returns the seeded content. The unit-test layer covers the
        // dashId-only-default → DefaultId resolution branch.
        using var client = _host.CreateClient();
        var dto = await client.GetFromJsonAsync<DashboardDto>(
            new Uri($"/api/v1/dashboards/{Dashboard.DefaultId}", UriKind.Relative), JsonOptions);

        dto!.Name.ShouldBe("Welcome from seed");
    }

    [Fact]
    public async Task Non_Default_Builtin_Is_Available_In_List()
    {
        using var client = _host.CreateClient();
        var list = await client.GetFromJsonAsync<DashboardDto[]>(
            new Uri("/api/v1/dashboards", UriKind.Relative), JsonOptions);

        list.ShouldNotBeNull();
        list!.Any(d => d.Name == "Team overview from seed").ShouldBeTrue();
    }

    /// <summary>
    /// Bootstraps a host with a temp packs root + isolated SQLite. The
    /// temp pack is populated *before* the host boots so the seeder
    /// picks up the dashboards on its single startup pass.
    /// </summary>
    public sealed class SeedingTestHost : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public string DatabasePath { get; } =
            Path.Combine(Path.GetTempPath(), $"oteldash-seedtest-{Guid.NewGuid():N}.db");

        public string PacksRoot { get; } =
            Path.Combine(Path.GetTempPath(), $"oteldash-seedtest-packs-{Guid.NewGuid():N}");

        public string ConnectionString => $"Data Source={DatabasePath}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var packDir = Path.Combine(PacksRoot, "seed");
            var dashboardsDir = Path.Combine(packDir, "dashboards");
            Directory.CreateDirectory(dashboardsDir);

            File.WriteAllText(Path.Combine(packDir, "pack.json"), """
            {
              "id": "seed",
              "name": "Seed pack",
              "version": "1.0.0",
              "libraries": [],
              "dashboards": [
                { "id": "default", "path": "dashboards/default.json", "builtin": true },
                { "id": "team",    "path": "dashboards/team.json",    "builtin": true }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(dashboardsDir, "default.json"), $$"""
            {
              "version": 1,
              "id": "{{Dashboard.DefaultId}}",
              "name": "Welcome from seed",
              "widgets": [
                {
                  "id": "33333333-3333-3333-3333-333333333301",
                  "kind": "std:text",
                  "x": 0, "y": 0, "w": 12, "h": 2,
                  "config": { "markdown": "## Hello", "align": "left" }
                }
              ]
            }
            """);
            File.WriteAllText(Path.Combine(dashboardsDir, "team.json"), """
            {
              "version": 1,
              "name": "Team overview from seed",
              "widgets": []
            }
            """);

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
                    ["Dashboard:Packs:Paths:0"] = PacksRoot,
                });
            });
        }

        public async Task InitializeAsync()
        {
            _ = Services;
            await using var scope = Services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            await context.Database.MigrateAsync();
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            TryDeleteFile(DatabasePath);
            TryDeleteDirectory(PacksRoot);
        }

        Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { /* ignore */ }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { /* ignore */ }
        }
    }
}
