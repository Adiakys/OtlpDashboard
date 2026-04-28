using OpenTelemetryDashboard.Core.Abstractions.Retention;

namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

public sealed class InMemoryMetricRetentionPolicy : IMetricRetentionPolicy
{
    private readonly InMemoryMetricStorage _storage;
    private readonly TimeProvider _timeProvider;

    public InMemoryMetricRetentionPolicy(
        InMemoryMetricStorage storage,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _storage = storage;
        _timeProvider = timeProvider;
    }

    public ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);

        var cutoff = _timeProvider.GetUtcNow() - maxAge;
        var dropped = _storage.TrimOlderThan(cutoff);
        return ValueTask.FromResult(dropped);
    }
}
