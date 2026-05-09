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
    /// <para>
    /// <paramref name="includeAttributes"/> controls whether each point's
    /// per-sample attribute map is hydrated. The map is the only large field
    /// in the row and is stored as a JSON string column, so deserialising it
    /// for queries that ignore the dimension (single-value widgets like Stat,
    /// Sparkline, Gauge) wastes most of the time on a wide window. Default
    /// is <c>false</c>; widgets that split-by an attribute key (Line,
    /// BarGauge, Pie, Heatmap) opt in.
    /// </para>
    /// </summary>
    Task<MetricSeriesSnapshot?> GetSeriesAsync(
        InstrumentKey key,
        MetricWindow window,
        int maxPoints,
        bool includeAttributes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Distinct non-null <c>service.name</c> values across the recorded
    /// instruments.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetDistinctServiceNamesAsync(CancellationToken cancellationToken);
}
