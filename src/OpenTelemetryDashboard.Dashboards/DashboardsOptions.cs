namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Runtime tuning for the dashboard built-in seeding. Bound from
/// <c>Dashboard:Dashboards</c> in configuration. Mirrors
/// <see cref="WidgetsOptions"/> in spirit: the first path in scan order is
/// the runtime-managed root (volume-backed), the rest are typically
/// baked-in image layers.
/// </summary>
public sealed class DashboardsOptions
{
    public const string SectionName = "Dashboard:Dashboards";

    /// <summary>
    /// Ordered list of directories the seeder scans for built-in dashboard
    /// JSON files at startup. First-wins on id collision: a dashboard file
    /// in an earlier path shadows the same id from a later path. Empty list
    /// means no seeding (apart from the empty default created if no
    /// dashboard with <c>Dashboard.DefaultId</c> exists yet).
    /// </summary>
    public List<string> BuiltinPaths { get; set; } = [];
}
