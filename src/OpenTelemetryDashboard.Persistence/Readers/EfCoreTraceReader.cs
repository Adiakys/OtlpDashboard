using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Readers;

public sealed class EfCoreTraceReader : ITraceReader
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;

    public EfCoreTraceReader(IDbContextFactory<TelemetryDbContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        _contextFactory = contextFactory;
    }

    public async Task<Span?> FindSpanAsync(TraceId traceId, SpanId spanId, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        return await context.Spans
            .AsNoTracking()
            .Include(s => s.Events)
            .Include(s => s.Links)
            .Where(s => s.TraceId == traceId && s.SpanId == spanId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async IAsyncEnumerable<(Span Span, string? ServiceName)> GetSpansInTraceAsync(
        TraceId traceId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Load the spans first (with events/links), then stitch in service
        // names via a second query. EF Core struggles to translate includes
        // alongside a Resource join in one projection, so we keep the
        // collection includes separate. The second query is bounded by the
        // distinct resource hashes in this trace — typically a handful.
        var spans = await context.Spans
            .AsNoTracking()
            .Include(s => s.Events)
            .Include(s => s.Links)
            .Where(s => s.TraceId == traceId)
            .OrderBy(s => s.StartUnixNano)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (spans.Count == 0)
        {
            yield break;
        }

        var hashes = spans.Select(s => s.ResourceHash).Distinct(ByteArrayEqualityComparer.Instance).ToArray();
        var serviceByHash = await context.Resources
            .AsNoTracking()
            .Where(r => hashes.Contains(r.Hash))
            .Select(r => new { r.Hash, r.ServiceName })
            .ToDictionaryAsync(x => x.Hash, x => x.ServiceName, ByteArrayEqualityComparer.Instance, cancellationToken)
            .ConfigureAwait(false);

        foreach (var span in spans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            serviceByHash.TryGetValue(span.ResourceHash, out var serviceName);
            yield return (span, serviceName);
        }
    }

    public async IAsyncEnumerable<(TraceSummary Summary, long SecondaryKey, string? ServiceName)> QueryTraceSummariesAsync(
        TraceQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var fromNano = UnixNanoTime.ToUnixNanoseconds(query.From);
        var toNano = UnixNanoTime.ToUnixNanoseconds(query.To);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseSpans = context.Spans
            .AsNoTracking()
            .Where(s => s.StartUnixNano >= fromNano && s.StartUnixNano < toNano);

        // Service-name filter: keep traces that contain at least one span whose
        // resource's `service.name` matches. This is "any-service" matching,
        // which is more useful for discovery (e.g. "show every trace that
        // touches frontend") than root-only filtering. The summary column
        // still shows the root's service so the UI stays consistent.
        if (!string.IsNullOrEmpty(query.ServiceName))
        {
            var service = query.ServiceName;
            var matchingTraceIds = context.Spans
                .AsNoTracking()
                .Where(s => s.StartUnixNano >= fromNano && s.StartUnixNano < toNano)
                .Join(
                    context.Resources.AsNoTracking(),
                    s => s.ResourceHash,
                    r => r.Hash,
                    (s, r) => new { s.TraceId, r.ServiceName })
                .Where(x => x.ServiceName == service)
                .Select(x => x.TraceId)
                .Distinct();
            baseSpans = baseSpans.Where(s => matchingTraceIds.Contains(s.TraceId));
        }

        var aggregateQuery = baseSpans
            .GroupBy(s => s.TraceId)
            .Select(g => new TraceAggregate
            {
                TraceId = g.Key,
                Start = g.Min(x => x.StartUnixNano),
                End = g.Max(x => x.EndUnixNano),
                Count = g.Count(),
                // MinSpanId is globally unique across traces (shadow Id is
                // monotonic). Using it as the cursor secondary key gives a
                // stable (Start, MinSpanId) DESC ordering.
                MinSpanId = g.Min(x => EF.Property<long>(x, "Id")),
            });

        if (query.After is { } cursor)
        {
            aggregateQuery = aggregateQuery.Where(a =>
                a.Start < cursor.Time ||
                (a.Start == cursor.Time && a.MinSpanId < cursor.SecondaryKey));
        }

        var aggregates = await aggregateQuery
            .OrderByDescending(a => a.Start)
            .ThenByDescending(a => a.MinSpanId)
            .Take(query.Limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (aggregates.Count == 0)
        {
            yield break;
        }

        var traceIds = aggregates.Select(a => a.TraceId).ToArray();
        var rootsByTrace = await LoadRootCandidatesAsync(context, traceIds, cancellationToken).ConfigureAwait(false);
        var resourceHashes = rootsByTrace.Values.Select(r => r.ResourceHash).Distinct(ByteArrayEqualityComparer.Instance).ToArray();
        var serviceByHash = await context.Resources
            .AsNoTracking()
            .Where(r => resourceHashes.Contains(r.Hash))
            .Select(r => new { r.Hash, r.ServiceName })
            .ToDictionaryAsync(x => x.Hash, x => x.ServiceName, ByteArrayEqualityComparer.Instance, cancellationToken)
            .ConfigureAwait(false);

        foreach (var aggregate in aggregates)
        {
            if (!rootsByTrace.TryGetValue(aggregate.TraceId, out var root))
            {
                continue;
            }

            var summary = new TraceSummary
            {
                TraceId = aggregate.TraceId,
                ResourceHash = root.ResourceHash,
                RootSpanName = root.Name,
                StartUnixNano = aggregate.Start,
                EndUnixNano = aggregate.End,
                SpanCount = aggregate.Count,
                RootStatusCode = root.StatusCode,
            };

            serviceByHash.TryGetValue(root.ResourceHash, out var serviceName);
            yield return (summary, aggregate.MinSpanId, serviceName);
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

        var query = context.Spans
            .AsNoTracking()
            .Where(s => s.StartUnixNano >= fromNano && s.StartUnixNano < toNano)
            .Join(
                context.Resources.AsNoTracking(),
                s => s.ResourceHash,
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

    /// <summary>
    /// Two-pass root resolution: first fetch spans with <c>ParentSpanId IS NULL</c>
    /// (the usual root), then for traces that lack an explicit root pick the
    /// earliest span. This avoids loading every span of every listed trace
    /// just to discover the display name.
    /// </summary>
    private static async Task<Dictionary<TraceId, RootCandidate>> LoadRootCandidatesAsync(
        TelemetryDbContext context,
        TraceId[] traceIds,
        CancellationToken cancellationToken)
    {
        var explicitRoots = await context.Spans
            .AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId) && s.ParentSpanId == null)
            .Select(s => new RootCandidate
            {
                TraceId = s.TraceId,
                Name = s.Name,
                ResourceHash = s.ResourceHash,
                StatusCode = s.StatusCode,
                StartUnixNano = s.StartUnixNano,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var roots = new Dictionary<TraceId, RootCandidate>();
        foreach (var candidate in explicitRoots)
        {
            // If a trace pathologically has multiple null-parent spans, prefer
            // the earliest one to keep the display stable.
            if (!roots.TryGetValue(candidate.TraceId, out var existing) ||
                candidate.StartUnixNano < existing.StartUnixNano)
            {
                roots[candidate.TraceId] = candidate;
            }
        }

        var missing = traceIds.Where(id => !roots.ContainsKey(id)).ToArray();
        if (missing.Length == 0)
        {
            return roots;
        }

        var fallbackCandidates = await context.Spans
            .AsNoTracking()
            .Where(s => missing.Contains(s.TraceId))
            .GroupBy(s => s.TraceId)
            .Select(g => g
                .OrderBy(x => x.StartUnixNano)
                .ThenBy(x => EF.Property<long>(x, "Id"))
                .Select(x => new RootCandidate
                {
                    TraceId = x.TraceId,
                    Name = x.Name,
                    ResourceHash = x.ResourceHash,
                    StatusCode = x.StatusCode,
                    StartUnixNano = x.StartUnixNano,
                })
                .First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var candidate in fallbackCandidates)
        {
            roots[candidate.TraceId] = candidate;
        }

        return roots;
    }

    private sealed class TraceAggregate
    {
        public TraceId TraceId { get; init; }
        public long Start { get; init; }
        public long End { get; init; }
        public int Count { get; init; }
        public long MinSpanId { get; init; }
    }

    private sealed class RootCandidate
    {
        public TraceId TraceId { get; init; }
        public string Name { get; init; } = string.Empty;
        public byte[] ResourceHash { get; init; } = [];
        public SpanStatusCode StatusCode { get; init; }
        public long StartUnixNano { get; init; }
    }
}
