using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests.Widgets;

/// <summary>
/// End-to-end tests for <c>/api/v1/packs</c> and the picker contract
/// at <c>/api/v1/widgets/libraries</c>. The fixture provisions a temp
/// packs root with a single hand-crafted pack so the registry has
/// realistic content without dragging in the demo bundle.
/// </summary>
public sealed class PackEndpointsTests : IClassFixture<PackEndpointsTests.PackTestHost>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PackTestHost _host;

    public PackEndpointsTests(PackTestHost host) => _host = host;

    [Fact]
    public async Task Get_Packs_Returns_Filesystem_Pack()
    {
        using var client = _host.CreateAuthenticatedClient();
        var packs = await client.GetFromJsonAsync<PackDto[]>(
            new Uri("/api/v1/packs", UriKind.Relative), JsonOptions);

        packs.ShouldNotBeNull();
        packs!.Length.ShouldBe(1);
        var pack = packs[0];
        pack.Id.ShouldBe("team");
        pack.InstallSource.ShouldBe(OpenTelemetryDashboard.Dashboards.Library.PackInstallSource.Filesystem);
        pack.Libraries.Count.ShouldBe(1);
        pack.Libraries[0].Id.ShouldBe("core");
    }

    [Fact]
    public async Task Get_Libraries_Surfaces_Library_With_PackId()
    {
        using var client = _host.CreateAuthenticatedClient();
        var libs = await client.GetFromJsonAsync<WidgetLibraryDto[]>(
            new Uri("/api/v1/widgets/libraries", UriKind.Relative), JsonOptions);

        libs.ShouldNotBeNull();
        libs!.Length.ShouldBe(1);
        libs[0].Id.ShouldBe("core");
        libs[0].PackId.ShouldBe("team");
        libs[0].Widgets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Install_Pack_Rejects_Disallowed_Host()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.PostAsJsonAsync(
            new Uri("/api/v1/packs/install", UriKind.Relative),
            new InstallPackRequest("https://evil.example/pack", "v1.0.0", null),
            JsonOptions);

        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Install_Pack_Rejects_Empty_Body()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.PostAsJsonAsync(
            new Uri("/api/v1/packs/install", UriKind.Relative),
            new InstallPackRequest("", "", null),
            JsonOptions);

        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Pack_Returns_400_For_Filesystem_Pack()
    {
        using var client = _host.CreateAuthenticatedClient();
        // The fixture's `team` pack has no .install.json — update must fail.
        var resp = await client.PostAsync(
            new Uri("/api/v1/packs/team/update", UriKind.Relative), content: null);

        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Pack_Returns_404_For_Unknown_Id()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.PostAsync(
            new Uri("/api/v1/packs/missing/update", UriKind.Relative), content: null);

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uninstall_Pack_Returns_404_For_Unknown_Id()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.DeleteAsync(
            new Uri("/api/v1/packs/missing", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Reload_Packs_Returns_204()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.PostAsync(
            new Uri("/api/v1/packs/reload", UriKind.Relative), content: null);

        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// Bootstraps a host whose <c>Dashboard:Packs:Paths:0</c> points
    /// at a temp dir we pre-populate with a single team pack. The pack
    /// has no <c>.install.json</c> so it presents as filesystem-installed.
    /// </summary>
    public sealed class PackTestHost : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public string DatabasePath { get; } =
            Path.Combine(Path.GetTempPath(), $"oteldash-pack-test-{Guid.NewGuid():N}.db");

        public string PacksPath { get; } =
            Path.Combine(Path.GetTempPath(), $"oteldash-pack-test-{Guid.NewGuid():N}");

        public string ConnectionString => $"Data Source={DatabasePath}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var teamPackDir = Path.Combine(PacksPath, "team");
            var libDir = Path.Combine(teamPackDir, "libraries", "core");
            var widgetDir = Path.Combine(libDir, "widgets", "p99");
            Directory.CreateDirectory(widgetDir);

            File.WriteAllText(Path.Combine(teamPackDir, "pack.json"), """
                {
                  "id": "team",
                  "name": "Team Pack",
                  "version": "1.0.0",
                  "libraries": [{ "id": "core", "path": "libraries/core" }],
                  "dashboards": []
                }
                """);
            File.WriteAllText(Path.Combine(libDir, "manifest.json"), """
                {"id":"core","name":"Core"}
                """);
            File.WriteAllText(Path.Combine(widgetDir, "widget.json"), """
                {"name":"p99","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}
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
                    ["Dashboard:Packs:Paths:0"] = PacksPath,
                    ["Dashboard:Auth:BrowserToken"] = "test-token",
                });
            });
        }

        public HttpClient CreateAuthenticatedClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
            return client;
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
            TryDeleteDirectory(PacksPath);
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
