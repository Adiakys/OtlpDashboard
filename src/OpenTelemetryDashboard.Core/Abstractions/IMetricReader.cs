using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Read-side contract for metric storage. Exposes the set of known instrument
/// keys, the associated instrument metadata, and the current ring-buffer
/// snapshot per key. Synchronous because the in-memory implementation is O(1).
/// DB-backed implementations (if/when added) may implement this asynchronously
/// through an extension of this contract.
/// </summary>
public interface IMetricReader
{
    IReadOnlyCollection<InstrumentKey> GetInstrumentKeys();

    Instrument? GetInstrument(InstrumentKey key);

    IReadOnlyList<DataPoint> GetPoints(InstrumentKey key);

    /// <summary>
    /// Returns the resource `service.name` associated with the given key
    /// (null when unset or unknown).
    /// </summary>
    string? GetServiceName(InstrumentKey key);

    /// <summary>
    /// Distinct non-null `service.name` values across currently-recorded
    /// instruments. Cheap: the store already indexes by key.
    /// </summary>
    IReadOnlyCollection<string> GetDistinctServiceNames();
}
