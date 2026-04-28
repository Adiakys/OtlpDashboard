using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Write-side contract for span storage. Implementations receive a window of
/// <see cref="TraceBatch"/> items accumulated by the ingestion pipeline and must
/// persist them atomically (per their own definition of "atomic").
/// </summary>
public interface ITraceSink
{
    Task WriteAsync(IReadOnlyList<TraceBatch> batches, CancellationToken cancellationToken);
}
