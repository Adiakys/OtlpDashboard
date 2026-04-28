using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

public sealed class InMemoryMetricReader : IMetricReader
{
    private readonly InMemoryMetricStorage _storage;

    public InMemoryMetricReader(InMemoryMetricStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    public IReadOnlyCollection<InstrumentKey> GetInstrumentKeys() => _storage.Keys;

    public Instrument? GetInstrument(InstrumentKey key) => _storage.GetInstrument(key);

    public IReadOnlyList<DataPoint> GetPoints(InstrumentKey key) => _storage.GetPoints(key);

    public string? GetServiceName(InstrumentKey key) => _storage.GetServiceName(key);

    public IReadOnlyCollection<string> GetDistinctServiceNames() => _storage.GetDistinctServiceNames();
}
