using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

/// <summary>
/// Drives <see cref="PackInstallService"/> against an in-process fake
/// <see cref="IGitInstaller"/>. The fake just creates a pack-shaped
/// directory on disk so the service's downstream pipeline (parse →
/// move → registry refresh) is exercised end-to-end without touching
/// libgit2 / the network.
/// </summary>
public sealed class PackInstallServiceTests : IDisposable
{
    private readonly string _root;

    public PackInstallServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"otel-dash-pack-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task InstallAsync_Successfully_Stages_Pack_And_Refreshes_Registry()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, registry) = NewService(fake);

        var pack = await service.InstallAsync(
            "https://github.com/org/team-pack", "v1.0.0", subPath: null, CancellationToken.None);

        pack.Id.ShouldBe("team-pack");
        pack.InstallSource.ShouldBe(PackInstallSource.Git);
        pack.GitUrl.ShouldBe("https://github.com/org/team-pack");
        pack.GitRefResolved.ShouldNotBeNullOrWhiteSpace();

        Directory.Exists(Path.Combine(_root, "team-pack")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "team-pack", ".install.json")).ShouldBeTrue();

        var packs = await registry.ListAsync(CancellationToken.None);
        packs.Count.ShouldBe(1);
        packs[0].Id.ShouldBe("team-pack");
    }

    [Fact]
    public async Task InstallAsync_Rejects_Non_AllowListed_Host()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);

        await Should.ThrowAsync<PackHostNotAllowedException>(
            () => service.InstallAsync(
                "https://evil.example/team-pack", "v1.0.0", subPath: null, CancellationToken.None));

        // Filesystem must be untouched on rejection.
        Directory.EnumerateFileSystemEntries(_root).Any().ShouldBeFalse();
    }

    [Fact]
    public async Task InstallAsync_Rejects_Non_Https_Scheme()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);

        await Should.ThrowAsync<PackHostNotAllowedException>(
            () => service.InstallAsync(
                "http://github.com/org/team-pack", "v1.0.0", subPath: null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_Throws_On_Id_Collision()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);

        await service.InstallAsync(
            "https://github.com/org/team-pack", "v1.0.0", subPath: null, CancellationToken.None);
        await Should.ThrowAsync<PackIdCollisionException>(
            () => service.InstallAsync(
                "https://github.com/org/team-pack", "v1.0.0", subPath: null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallAsync_With_SubPath_Re_Roots_Inside_Clone()
    {
        // Fake installer drops a "monorepo" with the pack at packs/team-pack.
        var fake = new FakeGitInstaller((targetDir, _, _) =>
        {
            var packDir = Path.Combine(targetDir, "packs", "team-pack");
            Directory.CreateDirectory(Path.Combine(packDir, "libraries", "core"));
            File.WriteAllText(Path.Combine(packDir, "pack.json"), """
                {
                  "id": "team-pack",
                  "name": "Team",
                  "version": "1.0.0",
                  "libraries": [{ "id": "core", "path": "libraries/core" }],
                  "dashboards": []
                }
                """);
            File.WriteAllText(Path.Combine(packDir, "libraries", "core", "manifest.json"),
                """{"id":"core","name":"Core"}""");
            // Sibling pack that should *not* be installed.
            var otherDir = Path.Combine(targetDir, "packs", "other-pack");
            Directory.CreateDirectory(Path.Combine(otherDir, "libraries", "core"));
            File.WriteAllText(Path.Combine(otherDir, "pack.json"), """
                {
                  "id": "other-pack",
                  "name": "Other",
                  "version": "1.0.0",
                  "libraries": [{ "id": "core", "path": "libraries/core" }],
                  "dashboards": []
                }
                """);
            File.WriteAllText(Path.Combine(otherDir, "libraries", "core", "manifest.json"),
                """{"id":"core","name":"Core"}""");
        });
        // The url ends with /monorepo so the default path-based id sniff
        // would say "monorepo" — but since we re-root via subPath, the
        // installer reads the pack.json at packs/team-pack and the on-
        // disk dir name is the resolved id "team-pack".
        var (service, registry) = NewService(fake);

        var pack = await service.InstallAsync(
            "https://github.com/org/monorepo", "v1.0.0", subPath: "packs/team-pack", CancellationToken.None);

        pack.Id.ShouldBe("team-pack");
        Directory.Exists(Path.Combine(_root, "team-pack")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_root, "other-pack")).ShouldBeFalse();

        var packs = await registry.ListAsync(CancellationToken.None);
        packs.Count.ShouldBe(1);
        packs[0].Id.ShouldBe("team-pack");
    }

    [Fact]
    public async Task InstallAsync_Rejects_Path_Traversal_In_SubPath()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);

        await Should.ThrowAsync<PackInstallPathInvalidException>(
            () => service.InstallAsync(
                "https://github.com/org/team-pack", "v1.0.0",
                subPath: "../escape", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_Updates_Existing_Pack()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);
        await service.InstallAsync(
            "https://github.com/org/team-pack", "v1.0.0", subPath: null, CancellationToken.None);

        fake.ResolveHeadResult = "fffffffffffffffffffffffffffffffffffff000";
        var updated = await service.UpdateAsync("team-pack", CancellationToken.None);

        updated.GitRefResolved.ShouldBe("fffffffffffffffffffffffffffffffffffff000");
    }

    [Fact]
    public async Task UpdateAsync_Throws_On_Non_Git_Pack()
    {
        // Drop a filesystem-only pack manually (no .install.json) and try to update it.
        var staticDir = Path.Combine(_root, "static");
        Directory.CreateDirectory(Path.Combine(staticDir, "libraries", "core"));
        File.WriteAllText(Path.Combine(staticDir, "pack.json"), """
            {
              "id": "static",
              "name": "Static",
              "version": "1.0.0",
              "libraries": [{ "id": "core", "path": "libraries/core" }],
              "dashboards": []
            }
            """);
        File.WriteAllText(Path.Combine(staticDir, "libraries", "core", "manifest.json"),
            """{"id":"core","name":"Core"}""");

        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);

        await Should.ThrowAsync<PackNotGitInstalledException>(
            () => service.UpdateAsync("static", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_Throws_NotFound_On_Unknown_Pack()
    {
        var fake = new FakeGitInstaller(WritePack);
        var (service, _) = NewService(fake);

        await Should.ThrowAsync<PackNotFoundException>(
            () => service.UpdateAsync("missing", CancellationToken.None));
    }

    // -------- Helpers ---------------------------------------------------

    private (PackInstallService Service, FilesystemPackRegistry Registry) NewService(IGitInstaller installer)
    {
        var opts = Options.Create(new PacksOptions
        {
            Paths = [_root],
            AllowedGitHosts = ["github.com"]
        });
        var registry = new FilesystemPackRegistry(
            opts, new TestHostEnvironment(), NullLogger<FilesystemPackRegistry>.Instance);
        var service = new PackInstallService(
            installer,
            registry,
            opts,
            registry,
            NullLogger<PackInstallService>.Instance);
        return (service, registry);
    }

    private static void WritePack(string targetDir, string url, string gitRef)
    {
        Directory.CreateDirectory(targetDir);
        var packId = Path.GetFileName(url.TrimEnd('/'));
        File.WriteAllText(Path.Combine(targetDir, "pack.json"), $$"""
            {
              "id": "{{packId}}",
              "name": "Team",
              "version": "{{gitRef}}",
              "libraries": [{ "id": "core", "path": "libraries/core" }],
              "dashboards": []
            }
            """);
        var libDir = Path.Combine(targetDir, "libraries", "core");
        Directory.CreateDirectory(libDir);
        File.WriteAllText(Path.Combine(libDir, "manifest.json"),
            """{"id":"core","name":"Core"}""");
    }

    private sealed class FakeGitInstaller : IGitInstaller
    {
        public string ResolveHeadResult { get; set; } = "abc1234567890abc1234567890abc1234567890";

        private readonly Action<string, string, string> _writeOnClone;

        // Constructor convenience: an Action over (targetDir, url, gitRef)
        // for tests that need to know the parameters.
        public FakeGitInstaller(Action<string, string, string> writeOnClone)
        {
            _writeOnClone = writeOnClone;
        }

        // Constructor convenience: a delegate that only cares about
        // (targetDir, url, gitRef) — folded into the canonical signature.
        public FakeGitInstaller(Action<string, string, string> writeOnClone, string resolveHead)
            : this(writeOnClone)
        {
            ResolveHeadResult = resolveHead;
        }

        public Task CloneAsync(string url, string gitRef, string targetDir, TimeSpan timeout, CancellationToken cancellationToken)
        {
            _writeOnClone(targetDir, url, gitRef);
            return Task.CompletedTask;
        }

        public Task FetchAndResetAsync(string repoDir, string gitRef, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public string ResolveHead(string repoDir) => ResolveHeadResult;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

}
