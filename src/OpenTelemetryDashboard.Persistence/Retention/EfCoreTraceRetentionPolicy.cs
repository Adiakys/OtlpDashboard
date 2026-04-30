using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Core.Common;

namespace OpenTelemetryDashboard.Persistence.Retention;

public sealed class EfCoreTraceRetentionPolicy : ITraceRetentionPolicy
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly TimeProvider _timeProvider;

    public EfCoreTraceRetentionPolicy(
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

        // Owned SpanEvents/SpanLinks cascade with the span row. Resource rows
        // referenced by the deleted spans are left intact on purpose — they may
        // still be referenced by log records.
        var totalRemoved = 0;
        while (true)
        {
            // Two-step (see EfCoreLogRetentionPolicy) — works on every provider
            // and keeps the DELETE statement bounded.
            var ids = await context.Spans
                .Where(s => s.StartUnixNano < cutoffNano)
                .OrderBy(s => s.StartUnixNano)
                .Select(s => EF.Property<long>(s, "Id"))
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (ids.Count == 0) break;

            var removed = await context.Spans
                .Where(s => ids.Contains(EF.Property<long>(s, "Id")))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            totalRemoved += removed;
            if (ids.Count < BatchSize) break;
        }
        return totalRemoved;
    }
}
