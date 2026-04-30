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

    /// <summary>See <c>EfCoreLogRetentionPolicy.BatchSize</c> for rationale.</summary>
    private const int BatchSize = 50_000;

    public async ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);

        var cutoffNano = UnixNanoTime.ToUnixNanoseconds(_timeProvider.GetUtcNow() - maxAge);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var dropped = 0;
        while (true)
        {
            // Two-step (see EfCoreLogRetentionPolicy).
            var ids = await context.MetricPoints
                .Where(p => p.TimeUnixNano < cutoffNano)
                .OrderBy(p => p.TimeUnixNano)
                .Select(p => p.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ids.Count == 0) break;

            var removed = await context.MetricPoints
                .Where(p => ids.Contains(p.Id))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            dropped += removed;
            if (ids.Count < BatchSize) break;
        }

        // Orphan instrument cleanup: drop dimension rows that lost all their
        // points so the listing endpoint doesn't surface dead instruments.
        // Cheap to evaluate — the FK index on (instrument_id, time) is the
        // covering index for the NOT EXISTS sub-query, and the orphan set is
        // bounded by the number of distinct instruments (≪ point count), so
        // a single round-trip stays small without batching.
        await context.Instruments
            .Where(i => !context.MetricPoints.Any(p => p.InstrumentId == i.Id))
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return dropped;
    }
}
