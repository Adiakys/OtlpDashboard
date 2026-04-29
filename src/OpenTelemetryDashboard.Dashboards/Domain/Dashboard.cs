namespace OpenTelemetryDashboard.Dashboards.Domain;

/// <summary>
/// A user-configured dashboard: a named collection of <see cref="DashboardWidget"/>
/// placements on a 12-column grid, bound to telemetry data. The per-widget
/// config payload is opaque JSON owned by the SPA — the backend stores and
/// round-trips it without interpreting the shape.
/// </summary>
public sealed class Dashboard
{
    /// <summary>
    /// Stable identifier of the seeded "default" dashboard. Created by the
    /// initial migration so a fresh install always has at least one
    /// dashboard, and the SPA can render without an empty-state branch.
    /// Protected from deletion at the API boundary.
    /// </summary>
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; init; }

    public string Name { get; init; } = "Default";

    /// <summary>
    /// Widgets placed on the dashboard's grid. Persisted as a separate
    /// table with a cascade FK; reconciled diff-wise on
    /// <see cref="Storage.IDashboardStore.UpdateAsync"/>.
    /// </summary>
    public List<DashboardWidget> Widgets { get; init; } = [];

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Optimistic concurrency token. Incremented on every save; mismatches
    /// surface as <see cref="Storage.DashboardConcurrencyException"/>.
    /// </summary>
    public uint RowVersion { get; init; }
}
