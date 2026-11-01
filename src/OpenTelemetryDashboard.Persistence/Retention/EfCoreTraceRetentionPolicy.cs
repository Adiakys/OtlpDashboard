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
            // Batches end at a timestamp, not an id list (see EfCoreLogRetentionPolicy).
            var boundary = await context.Spans
                .Where(s => s.StartUnixNano < cutoffNano)
                .OrderBy(s => s.StartUnixNano)
                .Skip(BatchSize)
                .Select(s => (long?)s.StartUnixNano)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            var removed = boundary is { } end
                ? await context.Spans.Where(s => s.StartUnixNano <= end).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false)
                : await context.Spans.Where(s => s.StartUnixNano < cutoffNano).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            totalRemoved += removed;
            if (boundary is null) break;
        }
        return totalRemoved;
    }
}
