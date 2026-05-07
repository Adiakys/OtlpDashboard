using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public sealed class FilesystemPackRegistryTests : IDisposable
{
    private readonly string _root;

    public FilesystemPackRegistryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"otel-dash-packs-{Guid.NewGuid():N}");
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
        using var registry = NewRegistry();

        var result = await registry.ListAsync(CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task Loads_Valid_Pack_With_Library_And_Dashboards()
    {
        WritePack("team", new PackJson(
            Id: "team",
            Name: "Team Pack",
            Version: "1.0.0",
            Libraries: [new LibRef("core", "libraries/core")],
            Dashboards: [new DashRef("default", "dashboards/default.json", Builtin: true)]));
        WriteLibrary("team", "libraries/core",
            """{"id":"core","name":"Core"}""");
        WriteWidget("team", "libraries/core", "p99",
            """{"name":"p99 latency","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");
        WriteDashboard("team", "dashboards/default.json",
            """{"version":1,"name":"Default","widgets":[]}""");

        using var registry = NewRegistry();
        var packs = await registry.ListAsync(CancellationToken.None);

        packs.Count.ShouldBe(1);
        var pack = packs[0];
        pack.Id.ShouldBe("team");
        pack.Version.ShouldBe("1.0.0");
        pack.InstallSource.ShouldBe(PackInstallSource.Filesystem);

        pack.Libraries.Count.ShouldBe(1);
        pack.Libraries[0].Id.ShouldBe("core");
        pack.Libraries[0].PackId.ShouldBe("team");
        pack.Libraries[0].Widgets.Count.ShouldBe(1);
        pack.Libraries[0].Widgets[0].KindId.ShouldBe("p99");
        pack.Libraries[0].Widgets[0].Engine.ShouldBe(WidgetEngine.Preset);

        pack.Dashboards.Count.ShouldBe(1);
        pack.Dashboards[0].Id.ShouldBe("default");
        pack.Dashboards[0].Builtin.ShouldBeTrue();
    }

    [Fact]
    public async Task Adapter_Surfaces_Libraries_From_Every_Pack()
    {
        WritePack("alpha", new PackJson(
            Id: "alpha",
            Name: "Alpha",
            Version: "1.0.0",
            Libraries: [new LibRef("core", "libraries/core")],
            Dashboards: []));
        WriteLibrary("alpha", "libraries/core", """{"id":"core","name":"Alpha core"}""");
        WriteWidget("alpha", "libraries/core", "x",
            """{"name":"X","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        WritePack("beta", new PackJson(
            Id: "beta",
            Name: "Beta",
            Version: "1.0.0",
            Libraries: [new LibRef("extras", "libraries/extras")],
            Dashboards: []));
        WriteLibrary("beta", "libraries/extras", """{"id":"extras","name":"Beta extras"}""");
        WriteWidget("beta", "libraries/extras", "y",
            """{"name":"Y","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        using var registry = NewRegistry();
        var adapter = new WidgetLibraryRegistryAdapter(registry);
        var libraries = await adapter.ListAsync(CancellationToken.None);

        libraries.Select(l => l.Id).OrderBy(s => s, StringComparer.Ordinal)
            .ShouldBe(["core", "extras"]);
    }

    [Fact]
    public async Task Pack_With_Bad_Manifest_Is_Skipped_Without_Affecting_Others()
    {
        WritePack("good", new PackJson(
            Id: "good", Name: "Good", Version: "1.0.0",
            Libraries: [new LibRef("core", "libraries/core")],
            Dashboards: []));
        WriteLibrary("good", "libraries/core", """{"id":"core","name":"Core"}""");
        WriteWidget("good", "libraries/core", "x",
            """{"name":"X","icon":"i-ph-target","engine":"preset","baseKind":"text"}""");

        // Pack with id != directory name — rejected by parser.
        WritePack("bad", new PackJson(
            Id: "different", Name: "Bad", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));

        using var registry = NewRegistry();
        var packs = await registry.ListAsync(CancellationToken.None);

        packs.Count.ShouldBe(1);
        packs[0].Id.ShouldBe("good");
    }

    [Fact]
    public async Task Reload_Picks_Up_New_Pack()
    {
        using var registry = NewRegistry();
        (await registry.ListAsync(CancellationToken.None)).ShouldBeEmpty();

        WritePack("late", new PackJson(
            Id: "late", Name: "Late", Version: "1.0.0",
            Libraries: [new LibRef("core", "libraries/core")],
            Dashboards: []));
        WriteLibrary("late", "libraries/core", """{"id":"core","name":"Core"}""");
        WriteWidget("late", "libraries/core", "stat",
            """{"name":"Stat","icon":"i-ph-target","engine":"preset","baseKind":"metric-stat"}""");

        // Without reload, cache is stale.
        (await registry.ListAsync(CancellationToken.None)).ShouldBeEmpty();

        await registry.ReloadAsync(CancellationToken.None);

        var refreshed = await registry.ListAsync(CancellationToken.None);
        refreshed.Count.ShouldBe(1);
        refreshed[0].Id.ShouldBe("late");
    }

    [Fact]
    public async Task Install_Json_Marks_Pack_As_Git_Source()
    {
        WritePack("from-git", new PackJson(
            Id: "from-git", Name: "From Git", Version: "2.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));
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

        using var registry = NewRegistry();
        var packs = await registry.ListAsync(CancellationToken.None);

        packs.Count.ShouldBe(1);
        var pack = packs[0];
        pack.InstallSource.ShouldBe(PackInstallSource.Git);
        pack.GitUrl.ShouldBe("https://github.com/org/from-git");
        pack.GitRef.ShouldBe("v2.0.0");
        pack.GitRefResolved.ShouldBe("abc123def");
        pack.InstalledAt.ShouldBe(installedAt);
    }

    [Fact]
    public async Task Uninstall_Removes_Pack_Directory_And_Refreshes_Cache()
    {
        WritePack("removable", new PackJson(
            Id: "removable", Name: "Removable", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));
        WritePack("keeper", new PackJson(
            Id: "keeper", Name: "Keeper", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));

        using var registry = NewRegistry();
        (await registry.ListAsync(CancellationToken.None)).Count.ShouldBe(2);

        await registry.UninstallAsync("removable", CancellationToken.None);

        Directory.Exists(Path.Combine(_root, "removable")).ShouldBeFalse();
        var after = await registry.ListAsync(CancellationToken.None);
        after.Select(l => l.Id).ShouldBe(["keeper"]);
    }

    [Fact]
    public async Task Uninstall_Unknown_Pack_Throws_NotFound()
    {
        using var registry = NewRegistry();

        await Should.ThrowAsync<PackNotFoundException>(
            () => registry.UninstallAsync("does-not-exist", CancellationToken.None));
    }

    [Fact]
    public async Task Uninstall_Of_BakedIn_Pack_Throws_NotRemovable()
    {
        var primary = Path.Combine(_root, "primary");
        var bakedIn = Path.Combine(_root, "baked-in");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(bakedIn);

        WritePackAt(bakedIn, "from-image", new PackJson(
            Id: "from-image", Name: "Image", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));

        var opts = Options.Create(new PacksOptions { Paths = [primary, bakedIn] });
        using var registry = new FilesystemPackRegistry(
            opts, new TestHostEnvironment(), NullLogger<FilesystemPackRegistry>.Instance);

        var packs = await registry.ListAsync(CancellationToken.None);
        packs.Count.ShouldBe(1);
        packs[0].Removable.ShouldBeFalse();

        await Should.ThrowAsync<PackNotRemovableException>(
            () => registry.UninstallAsync("from-image", CancellationToken.None));

        Directory.Exists(Path.Combine(bakedIn, "from-image")).ShouldBeTrue();
    }

    [Fact]
    public async Task Multiple_Paths_Are_Both_Scanned_And_First_Wins_On_Id_Collision()
    {
        var primary = Path.Combine(_root, "primary");
        var secondary = Path.Combine(_root, "secondary");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(secondary);

        WritePackAt(primary, "shared", new PackJson(
            Id: "shared", Name: "Shared (primary)", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));
        WritePackAt(primary, "alpha", new PackJson(
            Id: "alpha", Name: "Alpha", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));

        WritePackAt(secondary, "shared", new PackJson(
            Id: "shared", Name: "Shared (secondary)", Version: "9.9.9",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));
        WritePackAt(secondary, "beta", new PackJson(
            Id: "beta", Name: "Beta", Version: "1.0.0",
            Libraries: [new LibRef("stub", "libraries/stub")], Dashboards: []));

        var opts = Options.Create(new PacksOptions { Paths = [primary, secondary] });
        using var registry = new FilesystemPackRegistry(
            opts, new TestHostEnvironment(), NullLogger<FilesystemPackRegistry>.Instance);

        var packs = await registry.ListAsync(CancellationToken.None);

        packs.Select(p => p.Id).OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(["alpha", "beta", "shared"]);
        packs.First(p => p.Id == "shared").Name.ShouldBe("Shared (primary)");
    }

    private FilesystemPackRegistry NewRegistry()
    {
        var opts = Options.Create(new PacksOptions { Paths = [_root] });
        return new FilesystemPackRegistry(
            opts, new TestHostEnvironment(), NullLogger<FilesystemPackRegistry>.Instance);
    }

    // -------- pack.json helpers ----------------------------------------

    private void WritePack(string id, PackJson pack) => WritePackAt(_root, id, pack);

    private static void WritePackAt(string root, string id, PackJson pack)
    {
        var dir = Path.Combine(root, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "pack.json"), pack.ToJson());
    }

    // -------- nested-asset helpers -------------------------------------

    private void WriteLibrary(string packId, string relativePath, string manifestJson)
    {
        var dir = Path.Combine(_root, packId, relativePath);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifestJson);
    }

    private void WriteWidget(string packId, string libRelativePath, string kindId, string widgetJson)
    {
        var dir = Path.Combine(_root, packId, libRelativePath, "widgets", kindId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "widget.json"), widgetJson);
    }

    private void WriteDashboard(string packId, string relativePath, string json)
    {
        var path = Path.Combine(_root, packId, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private sealed record LibRef(string Id, string Path);

    private sealed record DashRef(string Id, string Path, bool Builtin);

    private sealed record PackJson(
        string Id,
        string Name,
        string Version,
        IReadOnlyList<LibRef> Libraries,
        IReadOnlyList<DashRef> Dashboards)
    {
        public string ToJson()
        {
            var libs = string.Join(",", Libraries.Select(l =>
                $$"""{"id":"{{l.Id}}","path":"{{l.Path}}"}"""));
            var dashes = string.Join(",", Dashboards.Select(d =>
                $$"""{"id":"{{d.Id}}","path":"{{d.Path}}","builtin":{{(d.Builtin ? "true" : "false")}}}"""));
            return $$"""
                {
                  "id": "{{Id}}",
                  "name": "{{Name}}",
                  "version": "{{Version}}",
                  "libraries": [{{libs}}],
                  "dashboards": [{{dashes}}]
                }
                """;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
