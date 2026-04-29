using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Read-side contract for metric storage. Async by design so the EF Core
/// implementation can serve queries without blocking. Mirrors the surface of
/// <see cref="ITraceReader"/> / <see cref="ILogReader"/>.
/// </summary>
public interface IMetricReader
{
    /// <summary>
    /// All instruments currently in the store with their point count and the
    /// service name resolved from the originating resource. Sorted by
    /// (ScopeName, Name, Kind) for deterministic UI presentation.
    /// </summary>
    Task<IReadOnlyList<InstrumentSummary>> ListInstrumentsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns the points for the instrument identified by <paramref name="key"/>,
    /// optionally filtered by <paramref name="window"/> (half-open <c>[From, To)</c>).
    /// Returns <c>null</c> if no instrument matches the key.
    /// </summary>
    Task<MetricSeriesSnapshot?> GetSeriesAsync(
        InstrumentKey key,
        MetricWindow? window,
        CancellationToken cancellationToken);

    /// <summary>
    /// Distinct non-null <c>service.name</c> values across the recorded
    /// instruments.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetDistinctServiceNamesAsync(CancellationToken cancellationToken);
}
