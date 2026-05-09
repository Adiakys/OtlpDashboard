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

    public async Task<TraceSpansSnapshot> GetSpansInTraceAsync(
        TraceId traceId,
        int maxSpans,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSpans);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Load the spans first (with events/links), then stitch in service
        // names via a second query. EF Core struggles to translate includes
        // alongside a Resource join in one projection, so we keep the
        // collection includes separate. The second query is bounded by the
        // distinct resource hashes in this trace — typically a handful.
        // We Take(maxSpans + 1) so a returned count of maxSpans + 1 means the
        // trace has at least one span we did not return (truncated).
        var spans = await context.Spans
            .AsNoTracking()
            .Include(s => s.Events)
            .Include(s => s.Links)
            .Where(s => s.TraceId == traceId)
            .OrderBy(s => s.StartUnixNano)
            .Take(maxSpans + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (spans.Count == 0)
        {
            return new TraceSpansSnapshot([], Truncated: false);
        }

        var truncated = spans.Count > maxSpans;
        if (truncated)
        {
            spans.RemoveAt(spans.Count - 1);
        }

        var hashes = spans.Select(s => s.ResourceHash).Distinct(ByteArrayEqualityComparer.Instance).ToArray();
        var serviceByHash = await context.Resources
            .AsNoTracking()
            .Where(r => hashes.Contains(r.Hash))
            .Select(r => new { r.Hash, r.ServiceName })
            .ToDictionaryAsync(x => x.Hash, x => x.ServiceName, ByteArrayEqualityComparer.Instance, cancellationToken)
            .ConfigureAwait(false);

        var rows = new List<TraceSpanRow>(spans.Count);
        foreach (var span in spans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            serviceByHash.TryGetValue(span.ResourceHash, out var serviceName);
            rows.Add(new TraceSpanRow(span, serviceName));
        }

        return new TraceSpansSnapshot(rows, truncated);
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

        // Service / unnamed filters share an EXISTS-correlated shape;
        // <see cref="ServiceMatchMode"/> just decides whether the
        // inner span has to be the root (default — matches the column
        // display, "deselect X" hides rows whose service column read
        // X) or any span in the trace ("any trace that touches X",
        // discovery oriented). The closure-captured boolean lifts to
        // a SQL parameter so the planner produces one parameterised
        // plan per query shape, not two.
        var rootOnly = query.ServiceMatch == ServiceMatchMode.Root;
        if (query.MatchUnnamedService)
        {
            var unnamedHashes = context.Resources
                .AsNoTracking()
                .Where(r => r.ServiceName == null || r.ServiceName == string.Empty)
                .Select(r => r.Hash);
            baseSpans = baseSpans.Where(s =>
                context.Spans
                    .AsNoTracking()
                    .Any(s2 =>
                        s2.TraceId == s.TraceId &&
                        (!rootOnly || s2.ParentSpanId == null) &&
                        s2.StartUnixNano >= fromNano && s2.StartUnixNano < toNano &&
                        unnamedHashes.Contains(s2.ResourceHash)));
        }
        else if (query.ServiceNames is { Count: > 0 } services)
        {
            // EF translates the `Contains` over the in-memory list to
            // `r.ServiceName IN (...)`; the indexed Resources.ServiceName
            // feeds the inner hash-set, the EXISTS short-circuits per
            // trace_id at the first matching span it finds.
            var serviceHashes = context.Resources
                .AsNoTracking()
                .Where(r => r.ServiceName != null && services.Contains(r.ServiceName))
                .Select(r => r.Hash);
            baseSpans = baseSpans.Where(s =>
                context.Spans
                    .AsNoTracking()
                    .Any(s2 =>
                        s2.TraceId == s.TraceId &&
                        (!rootOnly || s2.ParentSpanId == null) &&
                        s2.StartUnixNano >= fromNano && s2.StartUnixNano < toNano &&
                        serviceHashes.Contains(s2.ResourceHash)));
        }

        // Status filter — "any-span" semantics, mirrors the service filter:
        // "Error" matches traces containing at least one Error span; "Ok"
        // matches traces with no Error spans. Inconsistent with the Ok/Error
        // root badge in the column when an error happens deeper than the
        // root, but matches the discovery use case ("show me failing flows").
        if (query.StatusFilter is { } status)
        {
            if (status == TraceStatusFilter.Error)
            {
                baseSpans = baseSpans.Where(s =>
                    context.Spans
                        .AsNoTracking()
                        .Any(s2 =>
                            s2.TraceId == s.TraceId &&
                            s2.StartUnixNano >= fromNano && s2.StartUnixNano < toNano &&
                            s2.StatusCode == SpanStatusCode.Error));
            }
            else
            {
                baseSpans = baseSpans.Where(s =>
                    !context.Spans
                        .AsNoTracking()
                        .Any(s2 =>
                            s2.TraceId == s.TraceId &&
                            s2.StartUnixNano >= fromNano && s2.StartUnixNano < toNano &&
                            s2.StatusCode == SpanStatusCode.Error));
            }
        }

        // Span-name substring — also "any-span". Same rationale as the status
        // filter: makes the search box useful for "find trace touching X"
        // without tying the filter to root-only matching (which would force
        // root resolution before pagination — a much heavier restructure).
        if (!string.IsNullOrEmpty(query.SpanNameContains))
        {
            var pattern = $"%{EscapeLike(query.SpanNameContains)}%";
            baseSpans = baseSpans.Where(s =>
                context.Spans
                    .AsNoTracking()
                    .Any(s2 =>
                        s2.TraceId == s.TraceId &&
                        s2.StartUnixNano >= fromNano && s2.StartUnixNano < toNano &&
                        EF.Functions.Like(s2.Name, pattern)));
        }

        // Attribute filters — "any-span" semantics, AND across pairs:
        // each pair must be present on at least one span of the trace
        // (different pairs may be satisfied by different spans). Matches
        // the discovery use case "find me traces that touched key=value
        // anywhere". Implementation: one EXISTS-correlated subquery per
        // pair. The pattern matches the canonical `"key":"value"` JSON
        // substring on the spans' Attributes column via `EF.Property<string>`
        // (the converter's provider type is `string`, so the bypass is safe).
        if (query.AttributeFilters is { Count: > 0 } traceAttrs)
        {
            // Any-span semantics: each filter must be satisfied by at
            // least one span of the trace. AND across filters means
            // different filters can be satisfied by different spans
            // (matches the existing `service` and `spanNameContains`
            // behaviour). Each pair becomes its own EXISTS-correlated
            // subquery so the planner can short-circuit per trace.
            foreach (var filter in traceAttrs)
            {
                var key = filter.Key;
                var value = filter.Value;
                baseSpans = baseSpans.Where(s =>
                    context.Spans
                        .AsNoTracking()
                        .Any(s2 =>
                            s2.TraceId == s.TraceId &&
                            s2.StartUnixNano >= fromNano && s2.StartUnixNano < toNano &&
                            TelemetryDbFunctions.JsonAttributeEquals(
                                EF.Property<string>(s2, nameof(Span.Attributes)),
                                key,
                                value)));
            }
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

        // Duration filter on the aggregate (translates to a HAVING-style
        // predicate). Bounds are inclusive in milliseconds; we convert to
        // nanoseconds once here so the comparison stays integer-only.
        if (query.MinDurationMs is { } minMs)
        {
            var minNano = (long)(minMs * 1_000_000.0);
            aggregateQuery = aggregateQuery.Where(a => a.End - a.Start >= minNano);
        }
        if (query.MaxDurationMs is { } maxMs)
        {
            var maxNano = (long)(maxMs * 1_000_000.0);
            aggregateQuery = aggregateQuery.Where(a => a.End - a.Start <= maxNano);
        }

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

        // Distinct (TraceId, ServiceName) pairs across every span in
        // the listed traces — used to surface "root (+N other services)"
        // in the list column. One round-trip; bounded by the page
        // size × the number of services a trace touches (typically 1-3).
        // Filtered on the same time window so the planner stays on the
        // StartUnixNano index.
        var servicesByTrace = await context.Spans
            .AsNoTracking()
            .Where(s => traceIds.Contains(s.TraceId)
                && s.StartUnixNano >= fromNano && s.StartUnixNano < toNano)
            .Join(
                context.Resources.AsNoTracking(),
                s => s.ResourceHash,
                r => r.Hash,
                (s, r) => new { s.TraceId, r.ServiceName })
            .Where(x => x.ServiceName != null)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var allServicesByTrace = new Dictionary<TraceId, List<string>>();
        foreach (var pair in servicesByTrace)
        {
            if (!allServicesByTrace.TryGetValue(pair.TraceId, out var list))
            {
                list = new List<string>();
                allServicesByTrace[pair.TraceId] = list;
            }
            list.Add(pair.ServiceName!);
        }

        foreach (var aggregate in aggregates)
        {
            if (!rootsByTrace.TryGetValue(aggregate.TraceId, out var root))
            {
                continue;
            }

            serviceByHash.TryGetValue(root.ResourceHash, out var serviceName);

            // Drop the root's service from the "other services" list so
            // the UI never repeats it next to itself; sort to keep the
            // tooltip ordering deterministic across reloads.
            IReadOnlyList<string> otherServiceNames = [];
            if (allServicesByTrace.TryGetValue(aggregate.TraceId, out var allServices))
            {
                var others = allServices
                    .Where(s => !string.Equals(s, serviceName, StringComparison.Ordinal))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
                if (others.Count > 0) otherServiceNames = others;
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
                OtherServiceNames = otherServiceNames,
            };

            yield return (summary, aggregate.MinSpanId, serviceName);
        }
    }

    public async Task<IReadOnlyList<TraceAggregationRow>> AggregateTracesAsync(
        TraceAggregationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var fromNano = UnixNanoTime.ToUnixNanoseconds(query.From);
        var toNano = UnixNanoTime.ToUnixNanoseconds(query.To);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Aggregate operates on root spans only — `groupBy=name` is the
        // root span's name, which is what users mean by "endpoint" /
        // "operation". The service and attribute filters apply to the
        // root span (cleaner semantics than the trace-list's "any-span"
        // for an aggregation use case).
        var rootSpans = context.Spans
            .AsNoTracking()
            .Where(s => s.StartUnixNano >= fromNano && s.StartUnixNano < toNano)
            .Where(s => s.ParentSpanId == null);

        if (query.ServiceNames is { Count: > 0 } services)
        {
            // Aggregations apply the service filter to the root span
            // only — top-N "operations" are root-named, so it matches
            // the user's mental model of "which endpoints do these
            // services expose".
            var serviceHashes = context.Resources
                .AsNoTracking()
                .Where(r => r.ServiceName != null && services.Contains(r.ServiceName))
                .Select(r => r.Hash);
            rootSpans = rootSpans.Where(s => serviceHashes.Contains(s.ResourceHash));
        }

        if (query.AttributeFilters is { Count: > 0 } filters)
        {
            foreach (var filter in filters)
            {
                var key = filter.Key;
                var value = filter.Value;
                rootSpans = rootSpans.Where(s =>
                    TelemetryDbFunctions.JsonAttributeEquals(
                        EF.Property<string>(s, nameof(Span.Attributes)),
                        key,
                        value));
            }
        }

        // Aggregate in nanoseconds (integer math the SQLite translator
        // accepts), then convert to milliseconds client-side. Doing the
        // division inside the SELECT projection trips the EF SQLite
        // provider's translator on the combination of `Avg` + `Max`
        // over a computed expression — the cleanest workaround is to
        // pre-project the row to a flat shape and aggregate that.
        var aggregateShape = rootSpans
            .Select(s => new
            {
                s.Name,
                s.StatusCode,
                DurationNs = s.EndUnixNano - s.StartUnixNano
            })
            .GroupBy(x => x.Name)
            .Select(g => new
            {
                Key = g.Key,
                Count = (long)g.Count(),
                ErrorCount = (long)g.Count(x => x.StatusCode == SpanStatusCode.Error),
                AvgNs = g.Average(x => (double)x.DurationNs),
                MaxNs = g.Max(x => x.DurationNs)
            });

        // Order at the database, take limit. ErrorRate sorts by
        // (errorCount * 1.0 / count) descending — division-by-zero
        // is impossible because GROUP BY guarantees count ≥ 1.
        var ordered = query.SortBy switch
        {
            TraceAggregationMetric.Count => aggregateShape.OrderByDescending(r => r.Count),
            TraceAggregationMetric.AvgMs => aggregateShape.OrderByDescending(r => r.AvgNs),
            TraceAggregationMetric.MaxMs => aggregateShape.OrderByDescending(r => r.MaxNs),
            TraceAggregationMetric.ErrorRate => aggregateShape.OrderByDescending(r => r.ErrorCount * 1.0 / r.Count),
            _ => aggregateShape.OrderByDescending(r => r.Count),
        };

        var rows = await ordered
            .Take(query.Limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        const double NsPerMs = 1_000_000.0;
        return rows
            .Select(r => new TraceAggregationRow(
                r.Key,
                r.Count,
                r.ErrorCount,
                r.AvgNs / NsPerMs,
                r.MaxNs / NsPerMs))
            .ToList();
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

    /// <summary>
    /// Escape user input for LIKE: <c>%</c> and <c>_</c> are wildcards, <c>\</c>
    /// is the default escape character.
    /// </summary>
    private static string EscapeLike(string input)
    {
        return input
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
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
