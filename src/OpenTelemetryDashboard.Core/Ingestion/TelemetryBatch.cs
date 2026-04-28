using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Core.Ingestion;

public abstract record TelemetryBatch(IReadOnlyList<Resource> Resources);

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
