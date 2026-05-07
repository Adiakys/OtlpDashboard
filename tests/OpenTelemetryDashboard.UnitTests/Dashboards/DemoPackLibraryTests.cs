using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

/// <summary>
/// Smoke test for the in-repo demo pack at
/// <c>demo/packs/default/</c>. The pack directory is bind-mounted into
/// the dashboard container by docker compose; a regression that breaks
/// the pack manifest or one of the widget envelopes would only show up
/// after deploy without this guard.
/// </summary>
public sealed class DemoPackLibraryTests
{
    private static readonly string PackRoot = LocatePackRoot();
    private static readonly string LibrariesRoot = Path.Combine(PackRoot, "libraries");

    public static IEnumerable<object[]> Libraries() =>
        Directory
            .EnumerateDirectories(LibrariesRoot)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p)! });

    [Fact]
    public void Pack_Manifest_Is_Valid()
    {
        var raw = File.ReadAllText(Path.Combine(PackRoot, "pack.json"));

        var ok = LibraryManifestParser.TryParsePack(raw, "default", out var pack, out var error);

        ok.ShouldBeTrue(error);
        pack!.Id.ShouldBe("default");
        pack.Libraries.Count.ShouldBeGreaterThan(0);
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void Library_Manifest_Is_Valid(string libraryId)
    {
        var manifest = File.ReadAllText(Path.Combine(LibrariesRoot, libraryId, "manifest.json"));

        var ok = LibraryManifestParser.TryParseManifest(manifest, libraryId, out var header, out var error);

        ok.ShouldBeTrue(error);
        header!.Id.ShouldBe(libraryId);
        header.Name.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void All_Library_Widgets_Parse_Successfully(string libraryId)
    {
        var widgetsDir = Path.Combine(LibrariesRoot, libraryId, "widgets");
        var widgetDirs = Directory.EnumerateDirectories(widgetsDir).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        widgetDirs.Length.ShouldBeGreaterThan(0);

        foreach (var dir in widgetDirs)
        {
            var kindId = Path.GetFileName(dir);
            var raw = File.ReadAllText(Path.Combine(dir, "widget.json"));

            var ok = LibraryManifestParser.TryParseWidget(raw, kindId, out var widget, out var error);

            ok.ShouldBeTrue($"Widget '{libraryId}/{kindId}' failed to parse: {error}");
            widget!.KindId.ShouldBe(kindId);
        }
    }

    [Fact]
    public void Demo_Libraries_Cover_Both_Preset_And_Spec_Engines()
    {
        var engines = new HashSet<WidgetEngine>();
        foreach (var libDir in Directory.EnumerateDirectories(LibrariesRoot))
        {
            var widgetsDir = Path.Combine(libDir, "widgets");
            foreach (var widgetDir in Directory.EnumerateDirectories(widgetsDir))
            {
                var raw = File.ReadAllText(Path.Combine(widgetDir, "widget.json"));
                LibraryManifestParser.TryParseWidget(raw, Path.GetFileName(widgetDir), out var widget, out _);
                if (widget is not null) engines.Add(widget.Engine);
            }
        }

        engines.ShouldContain(WidgetEngine.Preset);
        engines.ShouldContain(WidgetEngine.Spec);
    }

    /// <summary>
    /// Walks up from the test assembly until a directory containing
    /// <c>OpenTelemetryDashboard.slnx</c> is found, then resolves
    /// <c>demo/packs/default</c> relative to it.
    /// </summary>
    private static string LocatePackRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "OpenTelemetryDashboard.slnx")))
            {
                return Path.Combine(dir, "demo", "packs", "default");
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException(
            $"Could not locate repository root (no OpenTelemetryDashboard.slnx) walking up from '{AppContext.BaseDirectory}'.");
    }
}
