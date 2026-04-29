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

        // Single query: instrument joined with resource (for the service
        // name) and aggregated point count via a left subquery. Keeps the
        // listing endpoint at one round-trip regardless of how many
        // instruments are in the store.
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
                PointCount = context.MetricPoints.Count(p => p.InstrumentId == i.Id),
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
                Convert.ToHexString(row.ResourceHash).ToLowerInvariant(),
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

            items.Add(new InstrumentSummary(key, instrument, row.PointCount, row.ServiceName));
        }

        return items;
    }

    public async Task<MetricSeriesSnapshot?> GetSeriesAsync(
        InstrumentKey key,
        MetricWindow? window,
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

        var totalCount = await context.MetricPoints
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

        var pointRows = await pointsQuery
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

        var points = new List<DataPoint>(pointRows.Count);
        foreach (var row in pointRows)
        {
            points.Add(new DataPoint
            {
                TimeUnixNano = row.TimeUnixNano,
                StartTimeUnixNano = row.StartTimeUnixNano,
                Value = row.Value,
                Attributes = row.Attributes,
            });
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

        return new MetricSeriesSnapshot(key, instrument, instrumentRow.ServiceName, totalCount, points);
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
