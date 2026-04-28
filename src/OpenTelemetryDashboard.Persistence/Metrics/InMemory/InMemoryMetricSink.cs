using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

public sealed class InMemoryMetricSink : IMetricSink
{
    private readonly InMemoryMetricStorage _storage;

    public InMemoryMetricSink(InMemoryMetricStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    public Task WriteAsync(IReadOnlyList<MetricBatch> batches, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Build a hex(hash) -> service.name lookup from the batch's
            // resources (usually 1–2 entries). Samples carry only the hex
            // key, so we resolve the service name here before recording.
            var serviceByHash = new Dictionary<string, string?>(batch.Resources.Count, StringComparer.Ordinal);
            foreach (var resource in batch.Resources)
            {
                var hex = Convert.ToHexString(resource.Hash).ToLowerInvariant();
                serviceByHash[hex] = resource.ServiceName;
            }

            foreach (var sample in batch.Samples)
            {
                serviceByHash.TryGetValue(sample.Key.ResourceHashHex, out var serviceName);
                _storage.TryRecord(sample.Key, sample.Instrument, sample.Point, serviceName);
            }
        }

        return Task.CompletedTask;
    }
}
