using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Metrics;

/// <summary>
/// Read-side projection used by <c>GetSeriesAsync</c>: instrument metadata,
/// the resolved service name, the lifetime point count for the instrument
/// (counted across the whole retention horizon, NOT the requested window —
/// callers that need the windowed count read <see cref="Points"/>.Count
/// instead), and the points that fall inside the optional time window,
/// ordered by ascending time.
/// </summary>
public sealed record MetricSeriesSnapshot(
    InstrumentKey Key,
    Instrument Instrument,
    string? ServiceName,
    /// <summary>`service.instance.id` carried so the SPA can show two
    /// same-named instruments coming from different resources (e.g. two
    /// databases scraped under the same `service.name=postgresql`)
    /// distinctly in the metric detail header.</summary>
    string? ServiceInstanceId,
    int LifetimePointCount,
    IReadOnlyList<DataPoint> Points,
    /// <summary>True when the reader hit the configured row cap and the
    /// returned <see cref="Points"/> are an early prefix of the requested
    /// window. The caller must surface this to the user — the chart is
    /// incomplete, not empty.</summary>
    bool Truncated);

/// <summary>
/// Half-open time window <c>[From, To)</c> used to slice a series.
/// </summary>
public readonly record struct MetricWindow(DateTimeOffset From, DateTimeOffset To);
