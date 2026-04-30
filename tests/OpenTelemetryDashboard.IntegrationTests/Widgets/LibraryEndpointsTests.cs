using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Library;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests.Widgets;

/// <summary>
/// Library endpoints rely on a dedicated test host so the fixture can prime
/// the libraries directory before <c>Program.Main</c> boots and the
/// singleton registry is constructed.
/// </summary>
public sealed class LibraryEndpointsTests : IClassFixture<LibraryEndpointsTests.LibrariesTestHost>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly LibrariesTestHost _host;

    public LibraryEndpointsTests(LibrariesTestHost host) => _host = host;

    [Fact]
    public async Task GetLibraries_Returns_Discovered_Libraries()
    {
        using var client = _host.CreateClient();

        var libs = await client.GetFromJsonAsync<WidgetLibraryDto[]>(
            new Uri("/api/v1/widgets/libraries", UriKind.Relative), JsonOptions);

        libs.ShouldNotBeNull();
        var ids = libs!.Select(l => l.Id).ToHashSet();
        ids.ShouldContain("alpha-pack");
        ids.ShouldNotContain("bad-pack"); // id mismatch — must be skipped

        var alpha = libs.First(l => l.Id == "alpha-pack");
        alpha.Name.ShouldBe("Alpha Pack");
        alpha.InstallSource.ShouldBe(LibraryInstallSource.Filesystem);
        alpha.Widgets.Count.ShouldBe(1);
        alpha.Widgets[0].KindId.ShouldBe("p99");
        alpha.Widgets[0].BaseKind.ShouldBe("metric-stat");
    }

    [Fact]
    public async Task Delete_Library_Removes_Directory_And_Drops_From_Listing()
    {
        using var client = _host.CreateClient();

        // Drop a fresh library so the test stays independent of seeded data.
        var libId = $"trash-{Guid.NewGuid():N}"[..20];
        var dir = Path.Combine(_host.LibrariesPath, libId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{"id":"{{libId}}","name":"Trash","version":"1.0.0"}""");
        var widgetDir = Path.Combine(dir, "widgets", "stat");
        Directory.CreateDirectory(widgetDir);
        File.WriteAllText(Path.Combine(widgetDir, "widget.json"),
            """{"name":"Stat","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");

        using var reload = await client.PostAsync(
            new Uri("/api/v1/widgets/libraries/reload", UriKind.Relative), content: null);
        reload.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var del = await client.DeleteAsync(
            new Uri($"/api/v1/widgets/libraries/{libId}", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Directory.Exists(dir).ShouldBeFalse();

        var libs = await client.GetFromJsonAsync<WidgetLibraryDto[]>(
            new Uri("/api/v1/widgets/libraries", UriKind.Relative), JsonOptions);
        libs.ShouldNotBeNull();
        libs!.Any(l => l.Id == libId).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_Unknown_Library_Returns_404()
    {
        using var client = _host.CreateClient();

        using var del = await client.DeleteAsync(
            new Uri("/api/v1/widgets/libraries/never-installed", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Library_With_Bad_Id_Returns_400()
    {
        using var client = _host.CreateClient();

        using var del = await client.DeleteAsync(
            new Uri("/api/v1/widgets/libraries/Bad..Name", UriKind.Relative));
        del.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Install_From_Git_Creates_Library_And_Returns_201()
    {
        _host.GitInstaller.SeedAction = dir =>
        {
            File.WriteAllText(Path.Combine(dir, "manifest.json"),
                """{"id":"installed-pack","name":"Installed Pack","version":"1.0.0"}""");
            var widgetDir = Path.Combine(dir, "widgets", "stat");
            Directory.CreateDirectory(widgetDir);
            File.WriteAllText(Path.Combine(widgetDir, "widget.json"),
                """{"name":"Stat","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        };
        _host.GitInstaller.HeadSha = "abc1234567890";

        using var client = _host.CreateClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/libraries/install", UriKind.Relative),
            new InstallLibraryRequest("https://github.com/org/installed-pack", "v1.0.0"),
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var dto = await response.Content.ReadFromJsonAsync<WidgetLibraryDto>(JsonOptions);
        dto.ShouldNotBeNull();
        dto!.Id.ShouldBe("installed-pack");
        dto.InstallSource.ShouldBe(LibraryInstallSource.Git);
        dto.GitRefResolved.ShouldBe("abc1234567890");

        Directory.Exists(Path.Combine(_host.LibrariesPath, "installed-pack")).ShouldBeTrue();
    }

    [Fact]
    public async Task Install_From_Disallowed_Host_Returns_400()
    {
        using var client = _host.CreateClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/libraries/install", UriKind.Relative),
            new InstallLibraryRequest("https://evil.example.com/pack", "main"),
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Install_With_Empty_Body_Returns_400()
    {
        using var client = _host.CreateClient();
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/libraries/install", UriKind.Relative),
            new InstallLibraryRequest("", ""),
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Of_Filesystem_Library_Returns_400()
    {
        using var client = _host.CreateClient();
        // alpha-pack is seeded by the fixture as a filesystem library — no
        // .install.json present, so update must refuse.
        using var response = await client.PostAsync(
            new Uri("/api/v1/widgets/libraries/alpha-pack/update", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Of_Unknown_Library_Returns_404()
    {
        using var client = _host.CreateClient();
        using var response = await client.PostAsync(
            new Uri("/api/v1/widgets/libraries/no-such-thing/update", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReloadLibraries_Picks_Up_New_Library_Without_Restart()
    {
        using var client = _host.CreateClient();

        // Add a brand-new library directory after the host has booted.
        var newLibId = $"hot-{Guid.NewGuid():N}"[..20];
        var dir = Path.Combine(_host.LibrariesPath, newLibId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{"id":"{{newLibId}}","name":"Hot","version":"1.0.0"}""");

        var widgetDir = Path.Combine(dir, "widgets", "stat");
        Directory.CreateDirectory(widgetDir);
        File.WriteAllText(Path.Combine(widgetDir, "widget.json"),
            """{"name":"Stat","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");

        // Without an explicit reload, the registry's cache is stale.
        var pre = await client.GetFromJsonAsync<WidgetLibraryDto[]>(
            new Uri("/api/v1/widgets/libraries", UriKind.Relative), JsonOptions);
        pre.ShouldNotBeNull();
        pre!.Any(l => l.Id == newLibId).ShouldBeFalse();

        using var reload = await client.PostAsync(
            new Uri("/api/v1/widgets/libraries/reload", UriKind.Relative),
            content: null);
        reload.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var post = await client.GetFromJsonAsync<WidgetLibraryDto[]>(
            new Uri("/api/v1/widgets/libraries", UriKind.Relative), JsonOptions);
        post.ShouldNotBeNull();
        post!.Any(l => l.Id == newLibId).ShouldBeTrue();
    }

    /// <summary>
    /// Bootstraps a host whose <c>Dashboard:Widgets:LibrariesPaths:0</c>
    /// points at a temp directory pre-populated with sample libraries.
    /// Independent from <see cref="TestHostFixture"/> so the two suites
    /// don't share a registry singleton.
    /// </summary>
    public sealed class LibrariesTestHost : WebApplicationFactory<Program>, IAsyncLifetime
    {
        public string DatabasePath { get; } =
            Path.Combine(Path.GetTempPath(), $"oteldash-libtest-{Guid.NewGuid():N}.db");

        public string LibrariesPath { get; } =
            Path.Combine(Path.GetTempPath(), $"oteldash-libtest-libs-{Guid.NewGuid():N}");

        public string ConnectionString => $"Data Source={DatabasePath}";

        /// <summary>
        /// Stand-in for the real LibGit2Sharp installer. Tests assign
        /// <c>SeedAction</c> + <c>HeadSha</c> before hitting the install
        /// endpoint to drive the response.
        /// </summary>
        public FakeGitInstaller GitInstaller { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // Seed the libraries path *before* the host boots so the
            // singleton registry hydrates from a populated directory on the
            // first GET.
            Directory.CreateDirectory(LibrariesPath);
            SeedLibraries();

            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Dashboard:Storage:Provider"] = "Sqlite",
                    ["ConnectionStrings:Sqlite"] = ConnectionString,
                    ["OpenTelemetryDashboard:Ingestion:Channel:Capacity"] = "1000",
                    ["OpenTelemetryDashboard:Ingestion:Channel:MaxBatchSize"] = "64",
                    ["OpenTelemetryDashboard:Ingestion:Channel:FlushIntervalMs"] = "50",
                    ["Dashboard:Widgets:LibrariesPaths:0"] = LibrariesPath,
                });
            });

            // Replace the real git installer with a fake that writes
            // straight to disk. Real network clone would make this suite
            // dependent on GitHub being reachable from CI.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGitInstaller>();
                services.AddSingleton<IGitInstaller>(GitInstaller);
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
            TryDeleteDirectory(LibrariesPath);
        }

        Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

        private void SeedLibraries()
        {
            // Valid library
            var alpha = Path.Combine(LibrariesPath, "alpha-pack");
            Directory.CreateDirectory(alpha);
            File.WriteAllText(Path.Combine(alpha, "manifest.json"),
                """{"id":"alpha-pack","name":"Alpha Pack","version":"1.0.0"}""");
            var alphaWidget = Path.Combine(alpha, "widgets", "p99");
            Directory.CreateDirectory(alphaWidget);
            File.WriteAllText(Path.Combine(alphaWidget, "widget.json"),
                """{"name":"p99 latency","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat","defaultConfig":{"calc":"last"}}""");

            // Invalid library — manifest id ≠ directory
            var bad = Path.Combine(LibrariesPath, "bad-pack");
            Directory.CreateDirectory(bad);
            File.WriteAllText(Path.Combine(bad, "manifest.json"),
                """{"id":"different","name":"Bad","version":"1.0.0"}""");
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { /* leave temp file behind on slow shutdown */ }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch (IOException) { /* leave temp dir behind on slow shutdown */ }
        }
    }

    public sealed class FakeGitInstaller : IGitInstaller
    {
        /// <summary>What to write into the cloned dir; defaults to a no-op.</summary>
        public Action<string> SeedAction { get; set; } = _ => { };

        /// <summary>SHA returned by <see cref="ResolveHead"/>.</summary>
        public string HeadSha { get; set; } = "deadbeef0000000000000000000000000000beef";

        public Task CloneAsync(string url, string gitRef, string targetDir, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(targetDir);
            SeedAction(targetDir);
            return Task.CompletedTask;
        }

        public Task FetchAndResetAsync(string repoDir, string gitRef, TimeSpan timeout, CancellationToken cancellationToken)
        {
            SeedAction(repoDir);
            return Task.CompletedTask;
        }

        public string ResolveHead(string repoDir) => HeadSha;
    }
}
