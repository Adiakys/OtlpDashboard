using OpenTelemetryDashboard.Dashboards.Seeding;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

/// <summary>
/// Smoke test for the in-repo sample dashboards at
/// `dashboards-demo/`. The folder is bind-mounted by docker-compose so
/// a regression that breaks one of the sample envelopes would only show
/// up after deploy without this guard.
/// </summary>
public sealed class DemoDashboardsTests
{
    private static readonly string DemoDir = LocateDemoDir();

    [Fact]
    public void Every_Demo_Dashboard_Parses_Successfully()
    {
        var files = Directory.EnumerateFiles(DemoDir, "*.json").ToArray();
        files.Length.ShouldBeGreaterThan(0);

        foreach (var path in files)
        {
            var raw = File.ReadAllText(path);
            var ok = DashboardSeedParser.TryParse(raw, out var file, out var error);
            ok.ShouldBeTrue($"{Path.GetFileName(path)}: {error}");
            file!.Name.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Default_Demo_File_Declares_DefaultId()
    {
        var path = Path.Combine(DemoDir, "default.json");
        File.Exists(path).ShouldBeTrue();

        DashboardSeedParser.TryParse(File.ReadAllText(path), out var file, out var error)
            .ShouldBeTrue(error);
        file!.Id.ShouldBe(OpenTelemetryDashboard.Dashboards.Domain.Dashboard.DefaultId);
    }

    private static string LocateDemoDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            if (File.Exists(Path.Combine(dir, "OpenTelemetryDashboard.slnx")))
            {
                return Path.Combine(dir, "dashboards-demo");
            }
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new InvalidOperationException(
            $"Could not locate repository root walking up from '{AppContext.BaseDirectory}'.");
    }
}
