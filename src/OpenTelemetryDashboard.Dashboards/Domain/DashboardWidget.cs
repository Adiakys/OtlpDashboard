namespace OpenTelemetryDashboard.Dashboards.Domain;

/// <summary>
/// One placement of a widget on a dashboard's grid. <see cref="Kind"/> picks
/// the SPA component (e.g. <c>metric-line</c>, <c>text</c>); the per-kind
/// payload travels in <see cref="ConfigJson"/> as an opaque document so the
/// backend stays agnostic to the widget catalog and clients can version
/// their own schemas.
/// </summary>
public sealed class DashboardWidget
{
    public Guid Id { get; init; }

    public Guid DashboardId { get; init; }

    /// <summary>
    /// Widget kind discriminator. Mirrors the SPA's <c>WidgetKind</c> union
    /// (e.g. <c>metric-stat</c>, <c>metric-line</c>, <c>metric-sparkline</c>,
    /// <c>text</c>). Stored as text so adding new kinds doesn't require a
    /// migration.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>Grid column index (0-based, 12-column layout).</summary>
    public int X { get; init; }

    /// <summary>Grid row index (0-based).</summary>
    public int Y { get; init; }

    /// <summary>Width in grid cells.</summary>
    public int W { get; init; }

    /// <summary>Height in grid cells.</summary>
    public int H { get; init; }

    /// <summary>
    /// Per-kind configuration document, owned by the SPA. The backend treats
    /// it as opaque text and only enforces a size cap at the validation
    /// layer.
    /// </summary>
    public string ConfigJson { get; init; } = "{}";
}
