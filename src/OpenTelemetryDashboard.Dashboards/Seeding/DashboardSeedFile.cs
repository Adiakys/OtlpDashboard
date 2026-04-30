using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Seeding;

/// <summary>
/// Strict, parser-validated representation of one built-in dashboard
/// JSON file. The seeder folds this into a <see cref="Dashboard"/>
/// (assigning the resolved id and current timestamps) before handing
/// off to <see cref="Storage.IDashboardStore.AddAsync"/>.
/// </summary>
public sealed record DashboardSeedFile(
    /// <summary>Optional explicit id from the JSON; null when the file
    /// omits it. The seeder falls back to <c>default.json</c> →
    /// <see cref="Dashboard.DefaultId"/> or a deterministic SHA-1 hash
    /// of the filename.</summary>
    Guid? Id,
    string Name,
    IReadOnlyList<DashboardSeedWidget> Widgets);

/// <summary>One widget placement parsed out of a dashboard seed file.
/// Mirrors <see cref="DashboardWidget"/> but with the JSON-side shape
/// (no <c>DashboardId</c>, opaque config carried as raw text).</summary>
public sealed record DashboardSeedWidget(
    Guid Id,
    string Kind,
    int X,
    int Y,
    int W,
    int H,
    string ConfigJson);
