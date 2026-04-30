using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Persistence.Readers;

public sealed class EfCoreMetricReader : IMetricReader
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;

    public EfCoreMetricReader(IDbContextFactory<TelemetryDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<InstrumentSummary>> ListInstrumentsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Two cheap round-trips beat the single query that EF used to emit:
        // a correlated `(SELECT COUNT(*) FROM metric_points WHERE instrument_id = i.id)`
        // per row balloons to N subqueries on the wire. A flat GROUP BY on
        // metric_points is one index scan; the instrument metadata join is
        // another. Both are bounded by the instrument count, not by points.
        var counts = await context.MetricPoints
            .AsNoTracking()
            .GroupBy(p => p.InstrumentId)
            .Select(g => new { InstrumentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.InstrumentId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

        var rows = await (
            from i in context.Instruments.AsNoTracking()
            join r in context.Resources.AsNoTracking() on i.ResourceHash equals r.Hash into rj
            from r in rj.DefaultIfEmpty()
            select new
            {
                i.Id,
                i.ResourceHash,
                i.ScopeName,
                i.ScopeVersion,
                i.Name,
                i.Kind,
                i.Description,
                i.Unit,
                i.IsMonotonic,
                i.Temporality,
                ServiceName = r != null ? r.ServiceName : null,
            })
            .OrderBy(x => x.ScopeName)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Kind)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = new List<InstrumentSummary>(rows.Count);
        foreach (var row in rows)
        {
            var key = new InstrumentKey(
                Convert.ToHexStringLower(row.ResourceHash),
                row.ScopeName,
                row.Name,
                row.Kind);

            var instrument = new Instrument
            {
                Name = row.Name,
                Description = row.Description,
                Unit = row.Unit,
                Kind = row.Kind,
                IsMonotonic = row.IsMonotonic,
                Temporality = row.Temporality,
            };

            counts.TryGetValue(row.Id, out var pointCount);
            items.Add(new InstrumentSummary(key, instrument, pointCount, row.ServiceName));
        }

        return items;
    }

    public async Task<MetricSeriesSnapshot?> GetSeriesAsync(
        InstrumentKey key,
        MetricWindow? window,
        bool includeAttributes,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        byte[] resourceHash;
        try
        {
            resourceHash = Convert.FromHexString(key.ResourceHashHex);
        }
        catch (FormatException)
        {
            return null;
        }

        var instrumentRow = await (
            from i in context.Instruments.AsNoTracking()
            where i.ResourceHash == resourceHash
                && i.ScopeName == key.ScopeName
                && i.Name == key.InstrumentName
                && i.Kind == key.Kind
            join r in context.Resources.AsNoTracking() on i.ResourceHash equals r.Hash into rj
            from r in rj.DefaultIfEmpty()
            select new
            {
                i.Id,
                i.Description,
                i.Unit,
                i.IsMonotonic,
                i.Temporality,
                ServiceName = r != null ? r.ServiceName : null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (instrumentRow is null)
        {
            return null;
        }

        // Lifetime count — does NOT honour the requested window. Surfaced via
        // MetricSeriesSnapshot.LifetimePointCount so the listing UI can flag
        // instruments that have data outside the current view.
        var lifetimeCount = await context.MetricPoints
            .AsNoTracking()
            .Where(p => p.InstrumentId == instrumentRow.Id)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        var pointsQuery = context.MetricPoints
            .AsNoTracking()
            .Where(p => p.InstrumentId == instrumentRow.Id);

        if (window is { } w)
        {
            var fromNano = UnixNanoTime.ToUnixNanoseconds(w.From);
            var toNano = UnixNanoTime.ToUnixNanoseconds(w.To);
            pointsQuery = pointsQuery.Where(p => p.TimeUnixNano >= fromNano && p.TimeUnixNano < toNano);
        }

        // Two projections so the JSON column is left out of the SELECT list
        // entirely when the caller doesn't want attributes — the EF value
        // converter only deserialises columns that come back from the
        // database, so omitting `p.Attributes` skips both the bytes on the
        // wire and the JSON parse for every row.
        List<DataPoint> points;
        if (includeAttributes)
        {
            var rows = await pointsQuery
                .OrderBy(p => p.TimeUnixNano)
                .Select(p => new
                {
                    p.TimeUnixNano,
                    p.StartTimeUnixNano,
                    p.Value,
                    p.Attributes,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            points = new List<DataPoint>(rows.Count);
            foreach (var row in rows)
            {
                points.Add(new DataPoint
                {
                    TimeUnixNano = row.TimeUnixNano,
                    StartTimeUnixNano = row.StartTimeUnixNano,
                    Value = row.Value,
                    Attributes = row.Attributes,
                });
            }
        }
        else
        {
            var rows = await pointsQuery
                .OrderBy(p => p.TimeUnixNano)
                .Select(p => new
                {
                    p.TimeUnixNano,
                    p.StartTimeUnixNano,
                    p.Value,
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            points = new List<DataPoint>(rows.Count);
            foreach (var row in rows)
            {
                points.Add(new DataPoint
                {
                    TimeUnixNano = row.TimeUnixNano,
                    StartTimeUnixNano = row.StartTimeUnixNano,
                    Value = row.Value,
                    Attributes = AttributeMap.Empty,
                });
            }
        }

        var instrument = new Instrument
        {
            Name = key.InstrumentName,
            Description = instrumentRow.Description,
            Unit = instrumentRow.Unit,
            Kind = key.Kind,
            IsMonotonic = instrumentRow.IsMonotonic,
            Temporality = instrumentRow.Temporality,
        };

        return new MetricSeriesSnapshot(key, instrument, instrumentRow.ServiceName, lifetimeCount, points);
    }

    public async Task<IReadOnlyCollection<string>> GetDistinctServiceNamesAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var names = await (
            from i in context.Instruments.AsNoTracking()
            join r in context.Resources.AsNoTracking() on i.ResourceHash equals r.Hash
            where r.ServiceName != null
            select r.ServiceName!)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return names;
    }
}
