using System.Diagnostics;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Core.Ingestion;

public abstract record TelemetryBatch(IReadOnlyList<Resource> Resources)
{
    /// <summary>
    /// Activity context of the OTLP ingest call that produced this batch, when
    /// instrumentation is active. Used by the background writer to attach a
    /// span link from the persistence activity back to the originating push,
    /// without forcing a parent/child relationship across the asynchronous
    /// channel boundary.
    /// </summary>
    public ActivityContext IngestActivityContext { get; init; }
}

public sealed record TraceBatch(
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<Span> Spans)
    : TelemetryBatch(Resources);

public sealed record LogBatch(
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<LogRecord> Records)
    : TelemetryBatch(Resources);

public sealed record MetricBatch(
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<MetricSample> Samples)
    : TelemetryBatch(Resources);

public sealed record MetricSample(
    InstrumentKey Key,
    Instrument Instrument,
    DataPoint Point);
