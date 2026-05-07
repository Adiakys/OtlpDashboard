namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// One dashboard shipped inside a <see cref="Pack"/>. The seeder turns
/// <see cref="Builtin"/> entries into pre-installed dashboards on first
/// boot; the rest stay as installable templates the user can clone.
/// </summary>
public sealed class PackDashboard
{
    /// <summary>Dashboard identifier inside the pack — used as a stable
    /// key for re-seeding and to namespace the dashboard within the
    /// pack manager UI. <c>[a-z0-9-]</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Absolute path of the dashboard JSON file on disk.</summary>
    internal string SourcePath { get; init; } = string.Empty;

    /// <summary>Raw JSON payload (already validated by the parser at
    /// load time) — kept as text so the seeder hands it to the storage
    /// layer without re-serializing.</summary>
    public required string RawJson { get; init; }

    /// <summary>True when the seeder must insert this dashboard as a
    /// built-in on first boot. Without it the dashboard is just a
    /// template the user can clone via the install / clone flow.</summary>
    public bool Builtin { get; init; }
}
