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

    [Fact]
    public async Task Get_Pack_Asset_Serves_Icon_Image()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/icons/postgres/postgres.svg", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.ShouldBe("image/svg+xml");
        var body = await resp.Content.ReadAsStringAsync();
        body.ShouldStartWith("<svg");
    }

    [Fact]
    public async Task Get_Pack_Asset_Sanitises_Hostile_Svg()
    {
        // End-to-end: a pack ships a hostile SVG; the asset endpoint must
        // not pass the active-content shapes through to the browser. The
        // CSP/nosniff/sandbox headers are the primary defence; the
        // sanitiser is the second line that also covers clients that
        // ignore CSP.
        using var client = _host.CreateAuthenticatedClient();
        using var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/icons/postgres/evil.svg", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();

        body.ShouldNotContain("<script", Case.Insensitive);
        body.ShouldNotContain("foreignObject", Case.Insensitive);
        body.ShouldNotContain("javascript:", Case.Insensitive);
        body.ShouldNotContain("onload", Case.Insensitive);
        body.ShouldNotContain("<animate", Case.Insensitive);
        // Same-document anchor href must survive — confirms we strip
        // schemes, not all hrefs.
        body.ShouldContain("#legitimate");
    }

    [Fact]
    public async Task Get_Pack_Asset_Sets_Strict_Csp_And_Nosniff()
    {
        // Anonymous endpoint serving SVG/PNG/WebP: even with the extension
        // whitelist, an SVG with inline <script> would otherwise execute
        // in the dashboard origin. The endpoint must override the SPA's
        // permissive CSP with a "nothing executes" policy and stop the
        // browser from MIME-sniffing the bytes.
        using var client = _host.CreateAuthenticatedClient();
        using var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/icons/postgres/postgres.svg", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        var csp = resp.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'none'");
        csp.ShouldContain("sandbox");
    }

    [Fact]
    public async Task Get_Pack_Asset_Rejects_Path_Traversal()
    {
        using var client = _host.CreateAuthenticatedClient();
        // Even after URL-decoding ../.. the asset endpoint should refuse
        // to serve files outside the pack root. The endpoint short-
        // circuits on the literal `..` substring before touching FS.
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/..%2F..%2Fpack.json", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Pack_Asset_Rejects_Non_Whitelisted_Extension()
    {
        using var client = _host.CreateAuthenticatedClient();
        // pack.json itself sits at the pack root and would resolve to a
        // valid file path — the extension whitelist is what stops the
        // endpoint becoming an arbitrary-file reader.
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/pack.json", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Pack_Asset_Returns_404_For_Unknown_Pack()
    {
        using var client = _host.CreateAuthenticatedClient();
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/missing/assets/icons/postgres/postgres.svg", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Pack_Asset_Is_Reachable_Without_Auth()
    {
        // SVG <image> tags fetch with the browser's own GET, no Bearer
        // header — applying the read-API policy to this route would
        // 401 every service-map icon. Asset content is public-by-design
        // (pack-shipped imagery), so the route opts out of auth at
        // mapping time. This test guards against accidentally putting
        // it back behind the policy.
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/icons/postgres/postgres.svg", UriKind.Relative));

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.ShouldBe("image/svg+xml");
    }

    // ----------------------------------------------------------------
    // Attack-vector matrix. The asset endpoint is anonymous on purpose
    // (browser SVG <image> requests carry no Bearer token), so each
    // mitigation has a dedicated test. Trip-wires:
    //
    //   1. extension whitelist  → only .svg/.png/.webp leave the box
    //   2. literal `..` / `\` / leading `/` filter
    //   3. URL-encoded traversal (`%2e%2e/`)
    //   4. absolute-path probe                       (no leading `/`)
    //   5. pack-id regex                              (no path injection in id)
    //   6. containment check after Path.GetFullPath
    //   7. symlink guard on the leaf file            (no exfil via /etc/passwd link)
    //   8. symlink guard on intermediate directories (no exfil via icons/ -> /tmp link)
    //
    // Anything passing all eight ends up reading a regular file inside
    // the pack root with a whitelisted extension — i.e. the icons we
    // already serve.
    // ----------------------------------------------------------------

    [Fact]
    public async Task Pack_Asset_Anon_Cannot_Read_Pack_Manifest()
    {
        // pack.json sits at the pack root and would resolve to a valid
        // file path — the .json extension blocks it. The endpoint is
        // anonymous, so this is the first line that stops a pack from
        // becoming an arbitrary-config reader for unauthenticated
        // callers.
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(new Uri("/api/v1/packs/team/assets/pack.json", UriKind.Relative));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pack_Asset_Anon_Cannot_Read_Install_Metadata()
    {
        // .install.json is the registry's git-install provenance file.
        // It can carry the source URL — not a credential, but not
        // intended to be public either. Same defence as pack.json: the
        // extension whitelist rejects it.
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(new Uri("/api/v1/packs/team/assets/.install.json", UriKind.Relative));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pack_Asset_Anon_Cannot_Read_Widget_Json()
    {
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/libraries/core/widgets/p99/widget.json", UriKind.Relative));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("../etc/passwd.svg")]
    [InlineData("..%2Fetc%2Fpasswd.svg")]            // url-encoded /
    [InlineData("%2e%2e/etc/passwd.svg")]            // url-encoded ..
    [InlineData("%2E%2E%2Fetc%2Fpasswd.svg")]        // both, uppercase
    [InlineData("foo/../../etc/passwd.svg")]         // mid-path traversal
    [InlineData("foo/..%2F..%2Fetc%2Fpasswd.svg")]   // mid-path encoded
    public async Task Pack_Asset_Anon_Rejects_Path_Traversal(string path)
    {
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(
            new Uri($"/api/v1/packs/team/assets/{path}", UriKind.Relative));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pack_Asset_Anon_Rejects_Absolute_Path()
    {
        // Without the leading-slash guard, Path.Combine("packroot", "/etc/passwd")
        // returns "/etc/passwd" verbatim on POSIX (Combine treats the
        // second arg as already absolute and discards the first).
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets//etc/passwd.svg", UriKind.Relative));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pack_Asset_Anon_Rejects_Backslash_Path()
    {
        using var client = _host.CreateClient();
        // Even on POSIX where `\` is a valid filename char, the literal
        // filter rejects it — mainly to keep platform-portable
        // semantics so a Windows host doesn't quietly accept
        // `..\..\..\Windows\...`.
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/team/assets/foo%5C..%5Cpassword.svg", UriKind.Relative));
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pack_Asset_Anon_Rejects_Bad_Pack_Id()
    {
        using var client = _host.CreateClient();
        var resp = await client.GetAsync(
            new Uri("/api/v1/packs/..%2Fother/assets/icons/postgres/postgres.svg", UriKind.Relative));
        // BadRequest because the regex check on the pack id fires
        // before any filesystem touch.
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pack_Asset_Anon_Rejects_Symlink_Leaf()
    {
        // Drop a symlink inside the pack pointing OUTSIDE the pack
        // root. Path.GetFullPath canonicalises strings only — without
        // an explicit reparse-point check the StartsWith containment
        // guard would pass and File.ReadAllBytes would happily exfil.
        var iconDir = Path.Combine(_host.PacksPath, "team", "icons", "postgres");
        var sneaky = Path.Combine(iconDir, "sneaky.svg");
        var outsideTarget = Path.Combine(Path.GetTempPath(),
            $"oteldash-asset-secret-{Guid.NewGuid():N}.svg");
        File.WriteAllText(outsideTarget, "<svg><!-- secret --></svg>");

        try
        {
            File.CreateSymbolicLink(sneaky, outsideTarget);

            using var client = _host.CreateClient();
            var resp = await client.GetAsync(
                new Uri("/api/v1/packs/team/assets/icons/postgres/sneaky.svg", UriKind.Relative));
            resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            if (File.Exists(sneaky)) File.Delete(sneaky);
            if (File.Exists(outsideTarget)) File.Delete(outsideTarget);
        }
    }

    [Fact]
    public async Task Pack_Asset_Anon_Rejects_Symlink_Intermediate_Directory()
    {
        // An intermediate-directory symlink is the sneakier form: the
        // leaf file is a regular .svg, but a parent dir links somewhere
        // outside the pack tree. The endpoint walks parents and rejects
        // any reparse point along the way.
        var teamRoot = Path.Combine(_host.PacksPath, "team");
        var outsideDir = Path.Combine(Path.GetTempPath(),
            $"oteldash-asset-link-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outsideDir);
        File.WriteAllText(Path.Combine(outsideDir, "leak.svg"), "<svg/>");

        var linkDir = Path.Combine(teamRoot, "linked-icons");
        try
        {
            Directory.CreateSymbolicLink(linkDir, outsideDir);

            using var client = _host.CreateClient();
            var resp = await client.GetAsync(
                new Uri("/api/v1/packs/team/assets/linked-icons/leak.svg", UriKind.Relative));
            resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            if (Directory.Exists(linkDir))
            {
                // Symlinks to directories on POSIX delete via File.Delete
                // (they're symlinks, not real dirs); on Windows
                // Directory.Delete works for both.
                try { File.Delete(linkDir); } catch { Directory.Delete(linkDir, recursive: false); }
            }
            if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir, recursive: true);
        }
    }

    [Fact]
    public async Task Get_Packs_Surfaces_Icon_Metadata()
    {
        using var client = _host.CreateAuthenticatedClient();
        var packs = await client.GetFromJsonAsync<PackDto[]>(
            new Uri("/api/v1/packs", UriKind.Relative), JsonOptions);

        packs.ShouldNotBeNull();
        packs!.Length.ShouldBe(1);
        var icons = packs[0].Icons;
        icons.Count.ShouldBe(1);
        icons[0].Id.ShouldBe("postgres");
        icons[0].ImageUrl.ShouldBe("/api/v1/packs/team/assets/icons/postgres/postgres.svg");
        icons[0].Match.Count.ShouldBe(2);
        icons[0].Match[0].ServiceName.ShouldBe("postgresql");
        icons[0].Match[1].NamePattern.ShouldBe("^postgres");
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
            var iconDir = Path.Combine(teamPackDir, "icons", "postgres");
            Directory.CreateDirectory(widgetDir);
            Directory.CreateDirectory(iconDir);

            File.WriteAllText(Path.Combine(teamPackDir, "pack.json"), """
                {
                  "id": "team",
                  "name": "Team Pack",
                  "version": "1.0.0",
                  "libraries": [{ "id": "core", "path": "libraries/core" }],
                  "dashboards": [],
                  "icons": [{ "id": "postgres", "path": "icons/postgres" }]
                }
                """);
            File.WriteAllText(Path.Combine(libDir, "manifest.json"), """
                {"id":"core","name":"Core"}
                """);
            File.WriteAllText(Path.Combine(widgetDir, "widget.json"), """
                {"name":"p99","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}
                """);
            File.WriteAllText(Path.Combine(iconDir, "icon.json"), """
                {
                  "id": "postgres",
                  "name": "PostgreSQL",
                  "image": "postgres.svg",
                  "match": [
                    { "serviceName": "postgresql" },
                    { "namePattern": "^postgres" }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(iconDir, "postgres.svg"),
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\"></svg>");

            // Hostile SVG used by the sanitisation tests. The asset endpoint
            // serves any file under the pack root that matches the extension
            // whitelist, so this file is reachable via /assets/icons/postgres/evil.svg
            // even though it isn't declared in pack.json. Carries every
            // active-content shape the sanitiser is expected to strip.
            File.WriteAllText(Path.Combine(iconDir, "evil.svg"),
                """
                <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"
                     viewBox="0 0 16 16" onload="alert(1)">
                  <script>alert('xss')</script>
                  <foreignObject><iframe src="javascript:alert(2)"/></foreignObject>
                  <a href="javascript:alert(3)"><circle cx="8" cy="8" r="4"/></a>
                  <use xlink:href="javascript:alert(4)"/>
                  <animate attributeName="href" to="javascript:alert(5)"/>
                  <a href="#legitimate"><circle cx="2" cy="2" r="1"/></a>
                </svg>
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
