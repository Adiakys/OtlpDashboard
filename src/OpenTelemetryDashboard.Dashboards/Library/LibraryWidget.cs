using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// A widget exposed by a library. Mirrors <see cref="WidgetDefinition"/>
/// but lives in memory only — libraries are loaded from disk on every
/// reload, never persisted to the database. The fully-qualified <c>kind</c>
/// the SPA writes into placements is <c>library:&lt;libraryId&gt;/&lt;KindId&gt;</c>.
/// </summary>
public sealed class LibraryWidget
{
    /// <summary>
    /// Slug identifying the widget within its library (matches the directory
    /// name under <c>widgets/</c>). Lowercase letters, digits, and hyphens.
    /// </summary>
    public required string KindId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required string Icon { get; init; }

    public required WidgetEngine Engine { get; init; }

    /// <summary>
    /// For <see cref="WidgetEngine.Preset"/>: the builtin kind being wrapped
    /// (e.g. <c>metric-stat</c>). Stored unprefixed.
    /// </summary>
    public string? BaseKind { get; init; }

    /// <summary>Opaque JSON config payload (preset seed values).</summary>
    public string? ConfigJson { get; init; }

    /// <summary>Opaque JSON spec payload (Vega-Lite for spec, layout DSL for composite).</summary>
    public string? SpecJson { get; init; }

    public int DefaultW { get; init; } = 3;

    public int DefaultH { get; init; } = 3;
}
