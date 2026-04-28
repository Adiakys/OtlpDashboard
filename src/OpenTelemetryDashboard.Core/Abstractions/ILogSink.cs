using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Write-side contract for log-record storage. See <see cref="ITraceSink"/> for
/// the general pipeline contract.
/// </summary>
public interface ILogSink
{
    Task WriteAsync(IReadOnlyList<LogBatch> batches, CancellationToken cancellationToken);
}
