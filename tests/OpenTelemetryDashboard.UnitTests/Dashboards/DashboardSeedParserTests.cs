using OpenTelemetryDashboard.Dashboards.Seeding;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public sealed class DashboardSeedParserTests
{
    [Fact]
    public void Parses_Minimal_Valid_Envelope()
    {
        const string json = """
        {
          "version": 1,
          "name": "Demo",
          "widgets": []
        }
        """;

        var ok = DashboardSeedParser.TryParse(json, out var file, out var error);

        ok.ShouldBeTrue(error);
        file!.Id.ShouldBeNull();
        file.Name.ShouldBe("Demo");
        file.Widgets.ShouldBeEmpty();
    }

    [Fact]
    public void Parses_Explicit_Id()
    {
        const string json = """
        {
          "version": 1,
          "id": "11111111-2222-3333-4444-555555555555",
          "name": "X",
          "widgets": []
        }
        """;

        var ok = DashboardSeedParser.TryParse(json, out var file, out var error);

        ok.ShouldBeTrue(error);
        file!.Id.ShouldBe(new Guid("11111111-2222-3333-4444-555555555555"));
    }

    [Fact]
    public void Rejects_Non_Guid_Id()
    {
        const string json = """{"version":1,"id":"not-a-guid","name":"X","widgets":[]}""";

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("Guid");
    }

    [Fact]
    public void Rejects_Wrong_Version()
    {
        const string json = """{"version":2,"name":"X","widgets":[]}""";

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("version");
    }

    [Fact]
    public void Rejects_Missing_Name()
    {
        const string json = """{"version":1,"widgets":[]}""";

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("name");
    }

    [Fact]
    public void Parses_Widget_With_Opaque_Config()
    {
        const string json = """
        {
          "version": 1,
          "name": "X",
          "widgets": [
            {
              "id": "00000000-0000-0000-0000-000000000aaa",
              "kind": "std:metric-stat",
              "x": 0, "y": 0, "w": 4, "h": 3,
              "config": { "metric": null, "range": "last-1h" }
            }
          ]
        }
        """;

        var ok = DashboardSeedParser.TryParse(json, out var file, out var error);

        ok.ShouldBeTrue(error);
        file!.Widgets.Count.ShouldBe(1);
        var w = file.Widgets[0];
        w.Kind.ShouldBe("std:metric-stat");
        w.W.ShouldBe(4);
        w.ConfigJson.ShouldContain("\"range\"");
    }

    [Fact]
    public void Rejects_Widget_With_Out_Of_Range_W()
    {
        const string json = """
        {
          "version": 1,
          "name": "X",
          "widgets": [{
            "id": "00000000-0000-0000-0000-000000000aaa",
            "kind": "k",
            "x": 0, "y": 0, "w": 99, "h": 3,
            "config": {}
          }]
        }
        """;

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("'w'");
    }

    [Fact]
    public void Rejects_Widget_Without_Config_Object()
    {
        const string json = """
        {
          "version": 1,
          "name": "X",
          "widgets": [{
            "id": "00000000-0000-0000-0000-000000000aaa",
            "kind": "k",
            "x": 0, "y": 0, "w": 1, "h": 1
          }]
        }
        """;

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("config");
    }

    [Fact]
    public void Rejects_File_Larger_Than_Cap()
    {
        var huge = new string('x', DashboardSeedParser.MaxFileBytes + 1);
        var json = $$"""{"version":1,"name":"X","widgets":[],"_pad":"{{huge}}"}""";

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("too large");
    }

    [Fact]
    public void Rejects_Widget_Config_Larger_Than_Cap()
    {
        var huge = new string('x', DashboardSeedParser.MaxConfigBytes + 1);
        var json = $$"""
        {
          "version": 1,
          "name": "X",
          "widgets": [{
            "id": "00000000-0000-0000-0000-000000000aaa",
            "kind": "k",
            "x": 0, "y": 0, "w": 1, "h": 1,
            "config": { "blob": "{{huge}}" }
          }]
        }
        """;

        var ok = DashboardSeedParser.TryParse(json, out _, out var error);

        ok.ShouldBeFalse();
        error!.ShouldContain("too large");
    }
}
