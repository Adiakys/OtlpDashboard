using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Core.Common;

namespace OpenTelemetryDashboard.Persistence.Retention;

public sealed class EfCoreLogRetentionPolicy : ILogRetentionPolicy
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public EfCoreLogRetentionPolicy(
        IDbContextFactory<TelemetryDbContext> contextFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Cap per chunk: large enough to amortise round-trips, small enough that
    /// the resulting DELETE doesn't keep a write lock long enough to back up
    /// the ingest channel. Tuned for SQLite where the writer is global; on
    /// PostgreSQL/SqlServer the same cap keeps each statement bounded so the
    /// retention sweep doesn't surprise the planner with a multi-million-row
    /// delete.
    /// </summary>
    private const int BatchSize = 50_000;

    public async ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxAge, TimeSpan.Zero);

        var cutoffNano = UnixNanoTime.ToUnixNanoseconds(_timeProvider.GetUtcNow() - maxAge);

        await using var context = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var totalRemoved = 0;
        while (true)
        {
            // Two-step: SELECT a bounded id batch, then DELETE by id list.
            // SQLite ships without SQLITE_ENABLE_UPDATE_DELETE_LIMIT, so a
            // direct `Take(...).ExecuteDelete()` would fail there. The id
            // fetch is cheap because (TimeUnixNano) is indexed.
            var ids = await context.Logs
                .Where(l => l.TimeUnixNano < cutoffNano)
                .OrderBy(l => l.TimeUnixNano)
                .Select(l => EF.Property<long>(l, "Id"))
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ids.Count == 0) break;

            var removed = await context.Logs
                .Where(l => ids.Contains(EF.Property<long>(l, "Id")))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            totalRemoved += removed;
            if (ids.Count < BatchSize) break;
        }
        return totalRemoved;
    }
}
