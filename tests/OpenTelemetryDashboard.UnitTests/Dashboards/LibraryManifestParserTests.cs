using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public sealed class LibraryManifestParserTests
{
    [Fact]
    public void Manifest_Parses_With_Optional_Fields()
    {
        const string json = """
        {
          "id": "team-pack",
          "name": "Team Pack",
          "version": "1.2.0",
          "author": "team@example.com",
          "license": "MIT",
          "description": "Curated widgets"
        }
        """;

        var ok = LibraryManifestParser.TryParseManifest(json, "team-pack", out var header, out var error);

        ok.ShouldBeTrue(error);
        header!.Id.ShouldBe("team-pack");
        header.Name.ShouldBe("Team Pack");
        header.Version.ShouldBe("1.2.0");
        header.Author.ShouldBe("team@example.com");
        header.License.ShouldBe("MIT");
        header.Description.ShouldBe("Curated widgets");
    }

    [Fact]
    public void Manifest_Rejects_Id_Mismatch_With_Directory()
    {
        const string json = """{"id":"foo","name":"X","version":"1.0.0"}""";

        var ok = LibraryManifestParser.TryParseManifest(json, "bar", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("does not match the directory name");
    }

    [Fact]
    public void Manifest_Rejects_Path_Traversal_Id()
    {
        const string json = """{"id":"../etc","name":"X","version":"1.0.0"}""";

        var ok = LibraryManifestParser.TryParseManifest(json, "../etc", out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Manifest_Rejects_Missing_Required_Fields()
    {
        const string json = """{"id":"foo"}""";

        var ok = LibraryManifestParser.TryParseManifest(json, "foo", out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Widget_Preset_Round_Trips_DefaultConfig()
    {
        const string json = """
        {
          "name": "SLA Tracker",
          "description": "p99 latency",
          "icon": "i-ph-target",
          "defaultSize": { "w": 4, "h": 3 },
          "engine": "preset",
          "baseKind": "metric-stat",
          "defaultConfig": {
            "calc": "last",
            "unitKind": "ms"
          }
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "sla-tracker", out var widget, out var error);

        ok.ShouldBeTrue(error);
        widget!.KindId.ShouldBe("sla-tracker");
        widget.Engine.ShouldBe(WidgetEngine.Preset);
        widget.BaseKind.ShouldBe("metric-stat");
        widget.DefaultW.ShouldBe(4);
        widget.DefaultH.ShouldBe(3);
        widget.ConfigJson.ShouldNotBeNull();
        widget.ConfigJson.ShouldContain("\"calc\"");
        widget.ConfigJson.ShouldContain("\"last\"");
        widget.SpecJson.ShouldBeNull();
    }

    [Fact]
    public void Widget_Spec_Stores_Raw_Spec_Json()
    {
        const string json = """
        {
          "name": "Trace heatmap",
          "icon": "i-ph-grid-four",
          "defaultSize": { "w": 6, "h": 4 },
          "engine": "spec",
          "spec": { "mark": "rect" }
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "trace-heatmap", out var widget, out var error);

        ok.ShouldBeTrue(error);
        widget!.Engine.ShouldBe(WidgetEngine.Spec);
        widget.BaseKind.ShouldBeNull();
        widget.SpecJson.ShouldNotBeNull();
        widget.SpecJson.ShouldContain("\"mark\"");
        widget.SpecJson.ShouldContain("\"rect\"");
    }

    [Fact]
    public void Widget_Preset_Without_BaseKind_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "preset"
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("baseKind");
    }

    [Fact]
    public void Widget_Preset_With_Library_Prefixed_BaseKind_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "preset",
          "baseKind": "library:foo/bar"
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("':' prefix");
    }

    [Fact]
    public void Widget_Preset_With_Unknown_BaseKind_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "preset",
          "baseKind": "metric-radar"
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("not a recognized builtin kind");
    }

    [Fact]
    public void Widget_Spec_Without_Spec_Field_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "spec"
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("'spec'");
    }

    [Fact]
    public void Widget_Bad_Icon_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "<script>alert(1)</script>",
          "engine": "preset",
          "baseKind": "metric-stat"
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("icon");
    }

    [Fact]
    public void Widget_Default_Size_Out_Of_Range_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "preset",
          "baseKind": "metric-stat",
          "defaultSize": { "w": 99, "h": 1 }
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("defaultSize.w");
    }

    [Fact]
    public void Widget_Oversized_Spec_Is_Rejected()
    {
        var huge = new string('x', LibraryManifestParser.MaxSpecBytes + 1);
        var json = $$"""
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "spec",
          "spec": { "data": "{{huge}}" }
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "x", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("too large");
    }

    [Fact]
    public void Widget_Bad_Kind_Id_Is_Rejected()
    {
        const string json = """
        {
          "name": "X",
          "icon": "i-ph-target",
          "engine": "preset",
          "baseKind": "metric-stat"
        }
        """;

        var ok = LibraryManifestParser.TryParseWidget(json, "../weird", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("must match");
    }
}
