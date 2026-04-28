namespace OpenTelemetryDashboard.Dashboards.Domain;

/// <summary>
/// A user-configured dashboard: a named collection of widgets bound to
/// telemetry data, persisted as an opaque JSON layout. The layout shape is
/// owned by the SPA — the backend stores and validates it as JSON only,
/// without knowing the per-widget config schema.
/// </summary>
public sealed class Dashboard
{
    /// <summary>
    /// Stable identifier of the singleton "default" dashboard. The store
    /// lazy-creates this row on first read so callers never see a 404.
    /// </summary>
    public static readonly Guid DefaultId = new("00000000-0000-0000-0000-000000000001");

    public Guid Id { get; set; }

    public string Name { get; set; } = "Default";

    /// <summary>
    /// Serialized <c>{ widgets: [...] }</c> document. Stored as text so the
    /// backend stays agnostic to widget kinds; clients version their own
    /// shape.
    /// </summary>
    public string LayoutJson { get; set; } = """{"widgets":[]}""";

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Incremented on every save; mismatches
    /// surface as <see cref="Storage.DashboardConcurrencyException"/>.
    /// </summary>
    public uint RowVersion { get; set; }
}
