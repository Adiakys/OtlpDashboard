using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Core.Common;

namespace OpenTelemetryDashboard.Persistence.Retention;

/// <summary>
/// Drops metric points older than the configured age and prunes instruments
/// that lost all their points so listings stay aligned with what's actually
/// queryable.
/// </summary>
public sealed class EfCoreMetricRetentionPolicy : IMetricRetentionPolicy
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public EfCoreMetricRetentionPolicy(
        IDbContextFactory<TelemetryDbContext> contextFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    public async ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);

        var cutoffNano = UnixNanoTime.ToUnixNanoseconds(_timeProvider.GetUtcNow() - maxAge);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var dropped = await context.MetricPoints
            .Where(p => p.TimeUnixNano < cutoffNano)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        // Orphan instrument cleanup: drop dimension rows that lost all their
        // points so the listing endpoint doesn't surface dead instruments.
        // Cheap to evaluate — the FK index on (instrument_id, time) is the
        // covering index for the NOT EXISTS sub-query.
        await context.Instruments
            .Where(i => !context.MetricPoints.Any(p => p.InstrumentId == i.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return dropped;
    }
}
