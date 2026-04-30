using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public sealed class FilesystemWidgetLibraryRegistryTests : IDisposable
{
    private readonly string _root;

    public FilesystemWidgetLibraryRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"otel-dash-libs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Empty_Path_Returns_Empty_List()
    {
        var registry = NewRegistry();

        var result = await registry.ListAsync(CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Loads_Valid_Library_With_Widgets()
    {
        WriteLibrary("team-pack", manifest: """
            {"id":"team-pack","name":"Team Pack","version":"1.0.0"}
            """);
        WriteWidget("team-pack", "p99", """
            {"name":"p99 latency","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}
            """);

        var registry = NewRegistry();
        var result = await registry.ListAsync(CancellationToken.None);

        result.Count.ShouldBe(1);
        var lib = result[0];
        lib.Id.ShouldBe("team-pack");
        lib.Name.ShouldBe("Team Pack");
        lib.InstallSource.ShouldBe(LibraryInstallSource.Filesystem);
        lib.Widgets.Count.ShouldBe(1);
        lib.Widgets[0].KindId.ShouldBe("p99");
        lib.Widgets[0].Engine.ShouldBe(WidgetEngine.Preset);
        lib.Widgets[0].BaseKind.ShouldBe("metric-stat");
    }

    [Fact]
    public async Task Library_With_Bad_Manifest_Is_Skipped_Without_Affecting_Others()
    {
        WriteLibrary("good", manifest: """{"id":"good","name":"Good","version":"1.0.0"}""");
        WriteWidget("good", "x", """{"name":"x","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        // The 'bad' lib has a manifest where id != directory name.
        WriteLibrary("bad", manifest: """{"id":"different","name":"Bad","version":"1.0.0"}""");

        var registry = NewRegistry();
        var result = await registry.ListAsync(CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("good");
    }

    [Fact]
    public async Task Bad_Widget_Is_Skipped_Without_Killing_Library()
    {
        WriteLibrary("pack", manifest: """{"id":"pack","name":"Pack","version":"1.0.0"}""");
        WriteWidget("pack", "ok", """{"name":"OK","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        // Wrong baseKind
        WriteWidget("pack", "broken", """{"name":"Broken","icon":"i-ph-x","engine":"preset","baseKind":"metric-radar"}""");

        var registry = NewRegistry();
        var result = await registry.ListAsync(CancellationToken.None);

        result.Count.ShouldBe(1);
        result[0].Widgets.Select(w => w.KindId).ShouldBe(["ok"]);
    }

    [Fact]
    public async Task Reload_Picks_Up_New_Library()
    {
        var registry = NewRegistry();
        (await registry.ListAsync(CancellationToken.None)).ShouldBeEmpty();

        WriteLibrary("late", manifest: """{"id":"late","name":"Late","version":"1.0.0"}""");
        WriteWidget("late", "stat", """{"name":"Stat","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");

        // Without reload, cache is stale.
        (await registry.ListAsync(CancellationToken.None)).ShouldBeEmpty();

        await registry.ReloadAsync(CancellationToken.None);

        var refreshed = await registry.ListAsync(CancellationToken.None);
        refreshed.Count.ShouldBe(1);
        refreshed[0].Id.ShouldBe("late");
    }

    [Fact]
    public async Task Install_Json_Marks_Library_As_Git_Source()
    {
        WriteLibrary("from-git", manifest: """{"id":"from-git","name":"From Git","version":"2.0.0"}""");
        WriteWidget("from-git", "s", """{"name":"S","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        var installedAt = new DateTimeOffset(2026, 4, 30, 10, 0, 0, TimeSpan.Zero);
        File.WriteAllText(Path.Combine(_root, "from-git", ".install.json"), $$"""
            {
              "source": "git",
              "url": "https://github.com/org/from-git",
              "ref": "v2.0.0",
              "refResolved": "abc123def",
              "installedAt": "{{installedAt:O}}"
            }
            """);

        var registry = NewRegistry();
        var result = await registry.ListAsync(CancellationToken.None);

        result.Count.ShouldBe(1);
        var lib = result[0];
        lib.InstallSource.ShouldBe(LibraryInstallSource.Git);
        lib.GitUrl.ShouldBe("https://github.com/org/from-git");
        lib.GitRef.ShouldBe("v2.0.0");
        lib.GitRefResolved.ShouldBe("abc123def");
        lib.InstalledAt.ShouldBe(installedAt);
    }

    [Fact]
    public async Task Uninstall_Removes_Library_Directory_And_Refreshes_Cache()
    {
        WriteLibrary("removable", """{"id":"removable","name":"Removable","version":"1.0.0"}""");
        WriteWidget("removable", "w", """{"name":"W","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");
        WriteLibrary("keeper", """{"id":"keeper","name":"Keeper","version":"1.0.0"}""");
        WriteWidget("keeper", "k", """{"name":"K","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        using var registry = NewRegistry();
        (await registry.ListAsync(CancellationToken.None)).Count.ShouldBe(2);

        await registry.UninstallAsync("removable", CancellationToken.None);

        Directory.Exists(Path.Combine(_root, "removable")).ShouldBeFalse();
        var after = await registry.ListAsync(CancellationToken.None);
        after.Select(l => l.Id).ShouldBe(["keeper"]);
    }

    [Fact]
    public async Task Uninstall_Unknown_Library_Throws_NotFound()
    {
        using var registry = NewRegistry();

        await Should.ThrowAsync<WidgetLibraryNotFoundException>(
            () => registry.UninstallAsync("does-not-exist", CancellationToken.None));
    }

    [Fact]
    public async Task Uninstall_Of_BakedIn_Library_Throws_NotRemovable()
    {
        var primary = Path.Combine(_root, "primary");
        var bakedIn = Path.Combine(_root, "baked-in");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(bakedIn);

        WriteLibraryAt(bakedIn, "from-image", """{"id":"from-image","name":"Image","version":"1.0.0"}""");
        WriteWidgetAt(bakedIn, "from-image", "w", """{"name":"W","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        var opts = Options.Create(new WidgetsOptions { LibrariesPaths = [primary, bakedIn] });
        using var registry = new FilesystemWidgetLibraryRegistry(
            opts, new TestHostEnvironment(), NullLogger<FilesystemWidgetLibraryRegistry>.Instance);

        var libs = await registry.ListAsync(CancellationToken.None);
        libs.Count.ShouldBe(1);
        libs[0].Removable.ShouldBeFalse();

        await Should.ThrowAsync<WidgetLibraryNotRemovableException>(
            () => registry.UninstallAsync("from-image", CancellationToken.None));

        Directory.Exists(Path.Combine(bakedIn, "from-image")).ShouldBeTrue();
    }

    [Fact]
    public async Task Multiple_Paths_Are_Both_Scanned_And_First_Wins_On_Id_Collision()
    {
        // Two roots, both contain a library with id "shared". The first
        // root in the configured list must win; the second is shadowed.
        var primary = Path.Combine(_root, "primary");
        var secondary = Path.Combine(_root, "secondary");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(secondary);

        // Primary holds "shared" with one widget, plus a unique "alpha".
        WriteLibraryAt(primary, "shared", """{"id":"shared","name":"Shared (primary)","version":"1.0.0"}""");
        WriteWidgetAt(primary, "shared", "p", """{"name":"P","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        WriteLibraryAt(primary, "alpha", """{"id":"alpha","name":"Alpha","version":"1.0.0"}""");
        WriteWidgetAt(primary, "alpha", "x", """{"name":"X","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        // Secondary holds the shadowed "shared" plus a unique "beta".
        WriteLibraryAt(secondary, "shared", """{"id":"shared","name":"Shared (secondary)","version":"9.9.9"}""");
        WriteWidgetAt(secondary, "shared", "q", """{"name":"Q","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        WriteLibraryAt(secondary, "beta", """{"id":"beta","name":"Beta","version":"1.0.0"}""");
        WriteWidgetAt(secondary, "beta", "y", """{"name":"Y","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        var opts = Options.Create(new WidgetsOptions
        {
            LibrariesPaths = [primary, secondary]
        });
        using var registry = new FilesystemWidgetLibraryRegistry(
            opts, new TestHostEnvironment(), NullLogger<FilesystemWidgetLibraryRegistry>.Instance);

        var libs = await registry.ListAsync(CancellationToken.None);

        libs.Select(l => l.Id).OrderBy(id => id, StringComparer.Ordinal).ShouldBe(["alpha", "beta", "shared"]);
        libs.First(l => l.Id == "shared").Name.ShouldBe("Shared (primary)");
    }

    private FilesystemWidgetLibraryRegistry NewRegistry()
    {
        var opts = Options.Create(new WidgetsOptions { LibrariesPaths = [_root] });
        var env = new TestHostEnvironment();
        return new FilesystemWidgetLibraryRegistry(opts, env, NullLogger<FilesystemWidgetLibraryRegistry>.Instance);
    }

    private static void WriteLibraryAt(string root, string id, string manifest)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest);
    }

    private static void WriteWidgetAt(string root, string libId, string kindId, string widgetJson)
    {
        var dir = Path.Combine(root, libId, "widgets", kindId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "widget.json"), widgetJson);
    }

    private void WriteLibrary(string id, string manifest)
    {
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifest);
    }

    private void WriteWidget(string libId, string kindId, string widgetJson)
    {
        var dir = Path.Combine(_root, libId, "widgets", kindId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "widget.json"), widgetJson);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
