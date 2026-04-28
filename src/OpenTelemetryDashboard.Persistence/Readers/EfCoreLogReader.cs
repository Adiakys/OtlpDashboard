using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Readers;

public sealed class EfCoreLogReader : ILogReader
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;

    public EfCoreLogReader(IDbContextFactory<TelemetryDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async IAsyncEnumerable<LogRecord> QueryRecentAsync(
        int take,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = context.Logs
            .AsNoTracking()
            .OrderByDescending(l => l.TimeUnixNano)
            .Take(take)
            .AsAsyncEnumerable();

        await foreach (var record in query.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    public async IAsyncEnumerable<(LogRecord Record, long SecondaryKey, string? ServiceName)> QueryAsync(
        LogQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var fromNano = UnixNanoTime.ToUnixNanoseconds(query.From);
        var toNano = UnixNanoTime.ToUnixNanoseconds(query.To);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = context.Logs
            .AsNoTracking()
            .Where(l => l.TimeUnixNano >= fromNano && l.TimeUnixNano < toNano);

        if (query.TraceId is { } traceId)
        {
            baseQuery = baseQuery.Where(l => l.TraceId == traceId);
        }

        if (query.After is { } cursor)
        {
            baseQuery = baseQuery.Where(l =>
                l.TimeUnixNano < cursor.Time ||
                (l.TimeUnixNano == cursor.Time && EF.Property<long>(l, "Id") < cursor.SecondaryKey));
        }

        // Resource join brings in service.name for per-row display and for the
        // optional `?service=` filter. Keeping the join in the same projection
        // avoids an N+1 round-trip per page.
        var joined = baseQuery.Join(
            context.Resources.AsNoTracking(),
            l => l.ResourceHash,
            r => r.Hash,
            (l, r) => new
            {
                Record = l,
                SecondaryKey = EF.Property<long>(l, "Id"),
                ServiceName = r.ServiceName
            });

        if (!string.IsNullOrEmpty(query.ServiceName))
        {
            var service = query.ServiceName;
            joined = joined.Where(x => x.ServiceName == service);
        }

        var projected = joined
            .OrderByDescending(x => x.Record.TimeUnixNano)
            .ThenByDescending(x => x.SecondaryKey)
            .Take(query.Limit + 1)
            .AsAsyncEnumerable();

        await foreach (var row in projected.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return (row.Record, row.SecondaryKey, row.ServiceName);
        }
    }

    public async IAsyncEnumerable<string> GetDistinctServiceNamesAsync(
        DateTimeOffset fromTime,
        DateTimeOffset toTime,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var fromNano = UnixNanoTime.ToUnixNanoseconds(fromTime);
        var toNano = UnixNanoTime.ToUnixNanoseconds(toTime);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = context.Logs
            .AsNoTracking()
            .Where(l => l.TimeUnixNano >= fromNano && l.TimeUnixNano < toNano)
            .Join(
                context.Resources.AsNoTracking(),
                l => l.ResourceHash,
                r => r.Hash,
                (_, r) => r.ServiceName)
            .Where(s => s != null)
            .Distinct()
            .AsAsyncEnumerable();

        await foreach (var name in query.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (name is not null) yield return name;
        }
    }
}
