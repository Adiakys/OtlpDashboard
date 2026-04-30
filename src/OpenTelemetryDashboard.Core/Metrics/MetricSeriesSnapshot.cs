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
    int LifetimePointCount,
    IReadOnlyList<DataPoint> Points);

/// <summary>
/// Half-open time window <c>[From, To)</c> used to slice a series.
/// </summary>
public readonly record struct MetricWindow(DateTimeOffset From, DateTimeOffset To);
