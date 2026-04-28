using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

/// <summary>
/// Shared mutable state between <see cref="InMemoryMetricSink"/> (write) and
/// <see cref="InMemoryMetricReader"/> (read). Registered as a singleton so that
/// sink and reader always operate on the same ring buffers.
/// </summary>
public sealed class InMemoryMetricStorage
{
    private readonly ConcurrentDictionary<InstrumentKey, Entry> _entries = new();
    private readonly InMemoryMetricStoreOptions _options;
    private readonly ILogger<InMemoryMetricStorage> _logger;
    private int _instrumentsDropped;

    public InMemoryMetricStorage(
        IOptions<InMemoryMetricStoreOptions> options,
        ILogger<InMemoryMetricStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Appends a point to the ring buffer of the given instrument key. If the
    /// key is new and the instrument cap is reached, the sample is dropped
    /// (periodically logged). <paramref name="serviceName"/> is captured once
    /// on first record for the key.
    /// </summary>
    public bool TryRecord(InstrumentKey key, Instrument instrument, DataPoint point, string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        ArgumentNullException.ThrowIfNull(point);

        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Points.Write(point);
            return true;
        }

        if (_entries.Count >= _options.MaxInstruments)
        {
            var dropped = Interlocked.Increment(ref _instrumentsDropped);
            if (dropped == 1 || dropped % 1000 == 0)
            {
                _logger.InstrumentDropped(_options.MaxInstruments, dropped);
            }
            return false;
        }

        var created = new Entry(instrument, serviceName, new RingBuffer<DataPoint>(_options.MaxPointsPerInstrument));
        var entry = _entries.GetOrAdd(key, created);
        entry.Points.Write(point);
        return true;
    }

    public IReadOnlyCollection<InstrumentKey> Keys => _entries.Keys.ToArray();

    public Instrument? GetInstrument(InstrumentKey key) =>
        _entries.TryGetValue(key, out var entry) ? entry.Instrument : null;

    public IReadOnlyList<DataPoint> GetPoints(InstrumentKey key) =>
        _entries.TryGetValue(key, out var entry) ? entry.Points.Snapshot() : [];

    public string? GetServiceName(InstrumentKey key) =>
        _entries.TryGetValue(key, out var entry) ? entry.ServiceName : null;

    public IReadOnlyCollection<string> GetDistinctServiceNames()
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var entry in _entries.Values)
        {
            if (!string.IsNullOrEmpty(entry.ServiceName)) set.Add(entry.ServiceName);
        }
        return set;
    }

    /// <summary>
    /// Drops every data point observed strictly before <paramref name="cutoff"/>.
    /// Instruments whose ring buffer is emptied as a result are removed entirely.
    /// Returns the total number of points dropped.
    /// </summary>
    public int TrimOlderThan(DateTimeOffset cutoff)
    {
        var cutoffNano = UnixNanoTime.ToUnixNanoseconds(cutoff);
        var droppedTotal = 0;

        foreach (var pair in _entries)
        {
            var dropped = pair.Value.Points.RemoveWhile(p => p.TimeUnixNano < cutoffNano);
            droppedTotal += dropped;

            if (pair.Value.Points.Count == 0)
            {
                _entries.TryRemove(pair);
            }
        }

        return droppedTotal;
    }

    private sealed record Entry(Instrument Instrument, string? ServiceName, RingBuffer<DataPoint> Points);
}

internal static partial class InMemoryMetricStorageLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "InMemoryMetricStorage reached MaxInstruments={MaxInstruments}. Dropped instruments so far: {Dropped}")]
    public static partial void InstrumentDropped(this ILogger logger, int maxInstruments, int dropped);
}
