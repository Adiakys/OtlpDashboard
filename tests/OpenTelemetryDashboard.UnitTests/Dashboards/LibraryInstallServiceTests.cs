using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

/// <summary>
/// Drives <see cref="LibraryInstallService"/> against an in-process fake
/// <see cref="IGitInstaller"/> that simulates clone by writing the
/// expected files directly into the target directory. Covers the
/// transactional contract: success leaves only a final `<id>/` directory,
/// every failure path scrubs the temp dir.
/// </summary>
public sealed class LibraryInstallServiceTests : IDisposable
{
    private readonly string _root;

    public LibraryInstallServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"otel-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Install_Writes_Library_And_Marks_It_As_Git_Source()
    {
        var (service, _) = NewService(installer: new FakeGitInstaller(seed =>
        {
            File.WriteAllText(Path.Combine(seed, "manifest.json"),
                """{"id":"team-pack","name":"Team Pack","version":"1.0.0"}""");
            var widgetDir = Path.Combine(seed, "widgets", "p99");
            Directory.CreateDirectory(widgetDir);
            File.WriteAllText(Path.Combine(widgetDir, "widget.json"),
                """{"name":"P99","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        }));

        var lib = await service.InstallAsync(
            "https://github.com/org/team-pack",
            "v1.0.0",
            CancellationToken.None);

        lib.Id.ShouldBe("team-pack");
        lib.InstallSource.ShouldBe(LibraryInstallSource.Git);
        lib.GitUrl.ShouldBe("https://github.com/org/team-pack");
        lib.GitRef.ShouldBe("v1.0.0");
        lib.GitRefResolved.ShouldNotBeNullOrEmpty();
        lib.Widgets.Count.ShouldBe(1);

        Directory.Exists(Path.Combine(_root, "team-pack")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "team-pack", ".install.json")).ShouldBeTrue();
        // No leftover temp directories
        Directory.EnumerateDirectories(_root)
            .Select(Path.GetFileName)
            .Any(n => n!.StartsWith(".tmp-", StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Install_From_Disallowed_Host_Throws_And_Touches_Nothing()
    {
        var (service, _) = NewService(installer: ThrowingInstaller.Instance);

        await Should.ThrowAsync<WidgetLibraryHostNotAllowedException>(() =>
            service.InstallAsync("https://gitea.example.com/org/pack", "main", CancellationToken.None));

        Directory.EnumerateDirectories(_root).ShouldBeEmpty();
    }

    [Fact]
    public async Task Install_From_Non_Https_Url_Throws()
    {
        var (service, _) = NewService(installer: ThrowingInstaller.Instance);

        await Should.ThrowAsync<WidgetLibraryHostNotAllowedException>(() =>
            service.InstallAsync("ssh://git@github.com/org/pack.git", "main", CancellationToken.None));
    }

    [Fact]
    public async Task Install_With_Manifest_Missing_Rolls_Back_Tmp_Dir()
    {
        // Fake clones a repo that never writes manifest.json — install
        // must fail and clean up the temp directory.
        var (service, _) = NewService(installer: new FakeGitInstaller(_ => { /* empty repo */ }));

        await Should.ThrowAsync<WidgetLibraryManifestInvalidException>(() =>
            service.InstallAsync("https://github.com/org/empty", "v1", CancellationToken.None));

        Directory.EnumerateDirectories(_root).ShouldBeEmpty();
    }

    [Fact]
    public async Task Install_With_Existing_Id_Returns_Collision()
    {
        Directory.CreateDirectory(Path.Combine(_root, "dup"));
        File.WriteAllText(Path.Combine(_root, "dup", "manifest.json"),
            """{"id":"dup","name":"Existing","version":"0.1.0"}""");

        var (service, _) = NewService(installer: new FakeGitInstaller(seed =>
        {
            File.WriteAllText(Path.Combine(seed, "manifest.json"),
                """{"id":"dup","name":"New","version":"2.0.0"}""");
        }));

        await Should.ThrowAsync<WidgetLibraryIdCollisionException>(() =>
            service.InstallAsync("https://github.com/org/dup", "v2", CancellationToken.None));

        // Existing dir is intact
        File.ReadAllText(Path.Combine(_root, "dup", "manifest.json"))
            .ShouldContain("Existing");
        // No leftover tmp dir
        Directory.EnumerateDirectories(_root)
            .Select(Path.GetFileName)
            .Any(n => n!.StartsWith(".tmp-", StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Install_Metadata_File_Carries_Schema_Version_1()
    {
        var (service, _) = NewService(installer: new FakeGitInstaller(seed =>
        {
            File.WriteAllText(Path.Combine(seed, "manifest.json"),
                """{"id":"v1-pack","name":"V1","version":"1.0.0"}""");
        }));

        await service.InstallAsync("https://github.com/org/v1-pack", "main", CancellationToken.None);

        var meta = File.ReadAllText(Path.Combine(_root, "v1-pack", ".install.json"));
        meta.ShouldContain("\"version\": 1");
        meta.ShouldContain("\"source\": \"git\"");
    }

    [Fact]
    public async Task Update_Of_Filesystem_Library_Throws_NotGitInstalled()
    {
        // Library on disk without `.install.json` — the registry surfaces
        // it as filesystem source. Update must refuse.
        Directory.CreateDirectory(Path.Combine(_root, "manual"));
        File.WriteAllText(Path.Combine(_root, "manual", "manifest.json"),
            """{"id":"manual","name":"Manual","version":"1.0.0"}""");

        var (service, _) = NewService(installer: ThrowingInstaller.Instance);

        await Should.ThrowAsync<WidgetLibraryNotGitInstalledException>(() =>
            service.UpdateAsync("manual", CancellationToken.None));
    }

    [Fact]
    public async Task Update_Of_Unknown_Library_Throws_NotFound()
    {
        var (service, _) = NewService(installer: ThrowingInstaller.Instance);

        await Should.ThrowAsync<WidgetLibraryNotFoundException>(() =>
            service.UpdateAsync("does-not-exist", CancellationToken.None));
    }

    [Fact]
    public async Task Update_Refreshes_RefResolved_In_Metadata()
    {
        // Pre-seed an existing git-installed library on disk.
        var libDir = Path.Combine(_root, "live");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "manifest.json"),
            """{"id":"live","name":"Live","version":"1.0.0"}""");
        File.WriteAllText(Path.Combine(libDir, ".install.json"),
            """{"version":1,"source":"git","url":"https://github.com/org/live","ref":"main","refResolved":"oldsha","installedAt":"2026-04-30T00:00:00Z"}""");

        var fake = new FakeGitInstaller(_ => { }, headSha: "newsha000000");
        var (service, _) = NewService(installer: fake);

        var lib = await service.UpdateAsync("live", CancellationToken.None);

        lib.GitRefResolved.ShouldBe("newsha000000");
        File.ReadAllText(Path.Combine(libDir, ".install.json"))
            .ShouldContain("newsha000000");
    }

    private (LibraryInstallService Service, FilesystemWidgetLibraryRegistry Registry) NewService(IGitInstaller installer)
    {
        var opts = Options.Create(new WidgetsOptions
        {
            LibrariesPaths = [_root],
            AllowedGitHosts = ["github.com"]
        });
        var registry = new FilesystemWidgetLibraryRegistry(
            opts, new TestHostEnvironment(),
            NullLogger<FilesystemWidgetLibraryRegistry>.Instance);
        var service = new LibraryInstallService(
            installer, registry, opts, registry,
            NullLogger<LibraryInstallService>.Instance);
        return (service, registry);
    }

    /// <summary>
    /// Fake installer that runs the supplied seed action against the
    /// target directory to simulate a clone, then returns a deterministic
    /// SHA from <see cref="ResolveHead"/>. Calls <c>FetchAndResetAsync</c>
    /// re-run the seed (so update tests can change file contents).
    /// </summary>
    private sealed class FakeGitInstaller(Action<string> seed, string? headSha = null) : IGitInstaller
    {
        public Task CloneAsync(string url, string gitRef, string targetDir, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(targetDir);
            seed(targetDir);
            return Task.CompletedTask;
        }

        public Task FetchAndResetAsync(string repoDir, string gitRef, TimeSpan timeout, CancellationToken cancellationToken)
        {
            seed(repoDir);
            return Task.CompletedTask;
        }

        public string ResolveHead(string repoDir) => headSha ?? "fake-sha-1234567890abcdef";
    }

    private sealed class ThrowingInstaller : IGitInstaller
    {
        public static readonly ThrowingInstaller Instance = new();
        public Task CloneAsync(string url, string gitRef, string targetDir, TimeSpan timeout, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called.");
        public Task FetchAndResetAsync(string repoDir, string gitRef, TimeSpan timeout, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Should not be called.");
        public string ResolveHead(string repoDir) => throw new InvalidOperationException("Should not be called.");
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
