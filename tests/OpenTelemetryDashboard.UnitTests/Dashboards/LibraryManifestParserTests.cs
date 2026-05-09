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
          "id": "core",
          "name": "Core",
          "description": "Curated widgets",
          "icon": "i-ph-stack"
        }
        """;

        var ok = LibraryManifestParser.TryParseManifest(json, "core", out var header, out var error);

        ok.ShouldBeTrue(error);
        header!.Id.ShouldBe("core");
        header.Name.ShouldBe("Core");
        header.Description.ShouldBe("Curated widgets");
        header.Icon.ShouldBe("i-ph-stack");
    }

    [Fact]
    public void Manifest_Rejects_Id_Mismatch_With_Directory()
    {
        const string json = """{"id":"foo","name":"X"}""";

        var ok = LibraryManifestParser.TryParseManifest(json, "bar", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("does not match the directory name");
    }

    [Fact]
    public void Manifest_Rejects_Path_Traversal_Id()
    {
        const string json = """{"id":"../etc","name":"X"}""";

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

    [Fact]
    public void Pack_Parses_Icons_Array()
    {
        const string json = """
        {
          "id": "default",
          "name": "Default",
          "version": "1.0.0",
          "icons": [
            { "id": "postgres", "path": "icons/postgres" }
          ]
        }
        """;

        var ok = LibraryManifestParser.TryParsePack(json, "default", out var pack, out var error);

        ok.ShouldBeTrue(error);
        pack!.Icons.Count.ShouldBe(1);
        pack.Icons[0].Id.ShouldBe("postgres");
        pack.Icons[0].RelativePath.ShouldBe("icons/postgres");
    }

    [Fact]
    public void Icon_Descriptor_Parses_With_Mixed_Match_Types()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "postgres.svg",
          "match": [
            { "serviceName": "postgresql" },
            { "namePattern": "^postgres" }
          ]
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "postgres", out var icon, out var error);

        ok.ShouldBeTrue(error);
        icon!.Image.ShouldBe("postgres.svg");
        icon.Match.Count.ShouldBe(2);
        icon.Match[0].ServiceName.ShouldBe("postgresql");
        icon.Match[0].NamePattern.ShouldBeNull();
        icon.Match[1].ServiceName.ShouldBeNull();
        icon.Match[1].NamePattern.ShouldBe("^postgres");
    }

    [Fact]
    public void Icon_Descriptor_Rejects_Empty_Match()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "postgres.svg",
          "match": []
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "postgres", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("at least one");
    }

    [Fact]
    public void Icon_Descriptor_Rejects_Match_With_Both_Fields()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "postgres.svg",
          "match": [
            { "serviceName": "postgres", "namePattern": "^pg" }
          ]
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "postgres", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("exactly one");
    }

    [Fact]
    public void Icon_Descriptor_Rejects_Bad_Image_Extension()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "postgres.gif",
          "match": [ { "serviceName": "postgres" } ]
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "postgres", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain(".svg");
    }

    [Fact]
    public void Icon_Descriptor_Rejects_Image_Path_Traversal()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "../../etc/passwd.svg",
          "match": [ { "serviceName": "postgres" } ]
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "postgres", out _, out var error);

        ok.ShouldBeFalse();
        error.ShouldNotBeNull();
    }

    [Fact]
    public void Icon_Descriptor_Rejects_Invalid_Regex()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "postgres.svg",
          "match": [ { "namePattern": "(unclosed" } ]
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "postgres", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("regex");
    }

    [Fact]
    public void Icon_Descriptor_Rejects_Id_Mismatch_With_Directory()
    {
        const string json = """
        {
          "id": "postgres",
          "name": "PostgreSQL",
          "image": "postgres.svg",
          "match": [ { "serviceName": "postgres" } ]
        }
        """;

        var ok = LibraryManifestParser.TryParseIconDescriptor(json, "redis", out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("does not match the directory name");
    }
}
