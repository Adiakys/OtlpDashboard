namespace OpenTelemetryDashboard.Persistence.Demo;

/// <summary>
/// Switch for the historical-data seeder. Defaults off — only the demo
/// docker-compose enables it (via <c>Dashboard__DemoSeed__Enabled=true</c>)
/// so a fresh dashboard boots with a week of plausible traces and logs
/// already in storage, instead of a screen full of "no data" overlays.
///
/// Production / customer deployments should leave this disabled: the
/// seeder writes synthetic spans and log records straight into the
/// configured storage provider, which is precisely what you don't want
/// in a real environment.
/// </summary>
public sealed class DemoSeedOptions
{
    public const string SectionName = "Dashboard:DemoSeed";

    /// <summary>
    /// Enables the seeder. Default <c>false</c>.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// How many days back to backfill. Default 7.
    /// </summary>
    public int Days { get; init; } = 7;

    /// <summary>
    /// Number of trace spans to generate (one per trace — single-span
    /// traces are enough for the dashboard's lists; the tree view shows
    /// them as one-row traces). Default 600.
    /// </summary>
    public int TraceCount { get; init; } = 600;

    /// <summary>
    /// Number of log records to generate. Default 3500.
    /// </summary>
    public int LogCount { get; init; } = 3500;
}
