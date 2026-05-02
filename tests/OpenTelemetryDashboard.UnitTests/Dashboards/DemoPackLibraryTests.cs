using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

/// <summary>
/// Smoke test for the in-repo sample widget libraries at
/// <c>demo/widget-libraries/</c>. Every subdirectory is bind-mounted into
/// the dashboard container by docker compose, so a regression that breaks
/// one of the manifests or widget envelopes would only show up after deploy
/// without this guard.
/// </summary>
public sealed class DemoPackLibraryTests
{
    private static readonly string LibrariesRoot = LocateLibrariesRoot();

    public static IEnumerable<object[]> Packs() =>
        Directory
            .EnumerateDirectories(LibrariesRoot)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p)! });

    [Theory]
    [MemberData(nameof(Packs))]
    public void Pack_Manifest_Is_Valid(string packId)
    {
        var manifest = File.ReadAllText(Path.Combine(LibrariesRoot, packId, "manifest.json"));

        var ok = LibraryManifestParser.TryParseManifest(manifest, packId, out var header, out var error);

        ok.ShouldBeTrue(error);
        header!.Id.ShouldBe(packId);
        header.Name.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void All_Pack_Widgets_Parse_Successfully(string packId)
    {
        var widgetsDir = Path.Combine(LibrariesRoot, packId, "widgets");
        var widgetDirs = Directory.EnumerateDirectories(widgetsDir).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        widgetDirs.Length.ShouldBeGreaterThan(0);

        foreach (var dir in widgetDirs)
        {
            var kindId = Path.GetFileName(dir);
            var raw = File.ReadAllText(Path.Combine(dir, "widget.json"));

            var ok = LibraryManifestParser.TryParseWidget(raw, kindId, out var widget, out var error);

            ok.ShouldBeTrue($"Widget '{packId}/{kindId}' failed to parse: {error}");
            widget!.KindId.ShouldBe(kindId);
        }
    }

    [Fact]
    public void Demo_Libraries_Cover_Both_Preset_And_Spec_Engines()
    {
        var engines = new HashSet<WidgetEngine>();
        foreach (var packDir in Directory.EnumerateDirectories(LibrariesRoot))
        {
            var widgetsDir = Path.Combine(packDir, "widgets");
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
    /// <c>demo/widget-libraries</c> relative to it.
    /// </summary>
    private static string LocateLibrariesRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "OpenTelemetryDashboard.slnx")))
            {
                return Path.Combine(dir, "demo", "widget-libraries");
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException(
            $"Could not locate repository root (no OpenTelemetryDashboard.slnx) walking up from '{AppContext.BaseDirectory}'.");
    }
}
