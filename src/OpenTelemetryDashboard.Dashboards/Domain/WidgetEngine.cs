using System.Text.Json.Serialization;

namespace OpenTelemetryDashboard.Dashboards.Domain;

/// <summary>
/// How a widget definition is realised at render time. Serialized over the
/// wire as the enum *name* (`"Preset"`, `"Spec"`, `"Composite"`) so the SPA
/// contract stays human-readable; the DB column stores the underlying
/// <see langword="int"/> via EF's <c>HasConversion&lt;int&gt;()</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WidgetEngine>))]
public enum WidgetEngine
{
    /// <summary>
    /// Pre-configured wrapper around a builtin widget kind. The
    /// <c>BaseKind</c> identifies the host renderer; <c>ConfigJson</c>
    /// carries the preset values the SPA seeds when an instance is added
    /// to a dashboard.
    /// </summary>
    Preset = 0,

    /// <summary>
    /// Vega-Lite spec-driven chart. <c>SpecJson</c> holds the spec; the
    /// SPA's <c>VegaLiteWidget</c> renders it. Wired in iter 2.
    /// </summary>
    Spec = 1,

    /// <summary>
    /// Composite layout — multiple builtin widgets arranged in a mini-grid.
    /// <c>SpecJson</c> holds the layout DSL. Wired in iter 5.
    /// </summary>
    Composite = 2
}
