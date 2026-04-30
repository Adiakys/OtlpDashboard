using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

/// <summary>
/// Smoke test for the in-repo sample library at
/// <c>widget-libraries-demo/demo-pack/</c>. The library is bind-mounted
/// into the container by docker compose, so a regression that breaks the
/// sample manifest would surface only after deploy without this guard.
/// </summary>
public sealed class DemoPackLibraryTests
{
    private static readonly string DemoPackPath = LocateDemoPack();

    [Fact]
    public void Demo_Pack_Manifest_Is_Valid()
    {
        var manifest = File.ReadAllText(Path.Combine(DemoPackPath, "manifest.json"));

        var ok = LibraryManifestParser.TryParseManifest(manifest, "demo-pack", out var header, out var error);

        ok.ShouldBeTrue(error);
        header!.Id.ShouldBe("demo-pack");
        header.Name.ShouldBe("Demo Pack");
    }

    [Fact]
    public void All_Demo_Widgets_Parse_Successfully()
    {
        var widgetsDir = Path.Combine(DemoPackPath, "widgets");
        var widgetDirs = Directory.EnumerateDirectories(widgetsDir).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        widgetDirs.Length.ShouldBeGreaterThan(0);

        foreach (var dir in widgetDirs)
        {
            var kindId = Path.GetFileName(dir);
            var raw = File.ReadAllText(Path.Combine(dir, "widget.json"));

            var ok = LibraryManifestParser.TryParseWidget(raw, kindId, out var widget, out var error);

            ok.ShouldBeTrue($"Demo widget '{kindId}' failed to parse: {error}");
            widget!.KindId.ShouldBe(kindId);
        }
    }

    [Fact]
    public void Demo_Pack_Includes_Both_Preset_And_Spec_Engines()
    {
        var widgetsDir = Path.Combine(DemoPackPath, "widgets");
        var engines = new HashSet<WidgetEngine>();
        foreach (var dir in Directory.EnumerateDirectories(widgetsDir))
        {
            var raw = File.ReadAllText(Path.Combine(dir, "widget.json"));
            LibraryManifestParser.TryParseWidget(raw, Path.GetFileName(dir), out var widget, out _);
            if (widget is not null) engines.Add(widget.Engine);
        }

        engines.ShouldContain(WidgetEngine.Preset);
        engines.ShouldContain(WidgetEngine.Spec);
    }

    /// <summary>
    /// Walks up from the test assembly until a directory containing
    /// <c>OpenTelemetryDashboard.slnx</c> is found, then resolves
    /// <c>widget-libraries-demo/demo-pack</c> relative to it.
    /// </summary>
    private static string LocateDemoPack()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "OpenTelemetryDashboard.slnx")))
            {
                return Path.Combine(dir, "widget-libraries-demo", "demo-pack");
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException(
            $"Could not locate repository root (no OpenTelemetryDashboard.slnx) walking up from '{AppContext.BaseDirectory}'.");
    }
}
