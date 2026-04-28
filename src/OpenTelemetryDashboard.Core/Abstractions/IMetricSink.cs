using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Write-side contract for metric storage. Implementations receive a window of
/// <see cref="MetricBatch"/> items accumulated by the ingestion pipeline.
/// The in-memory implementation appends to a per-instrument ring buffer; a
/// future relational implementation may persist them durably.
/// </summary>
public interface IMetricSink
{
    Task WriteAsync(IReadOnlyList<MetricBatch> batches, CancellationToken cancellationToken);
}
