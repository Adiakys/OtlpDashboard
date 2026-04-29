using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Write-side contract for metric storage. Implementations receive a window of
/// <see cref="MetricBatch"/> items accumulated by the ingestion pipeline and
/// persist them through their backing store.
/// </summary>
public interface IMetricSink
{
    Task WriteAsync(IReadOnlyList<MetricBatch> batches, CancellationToken cancellationToken);
}
