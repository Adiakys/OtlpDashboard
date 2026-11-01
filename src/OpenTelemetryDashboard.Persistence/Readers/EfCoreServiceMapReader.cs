using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Readers;

/// <summary>
/// EF Core implementation of <see cref="IServiceMapReader"/>. Backed by
/// the same span store the trace reader uses — querying spans is just
/// the storage detail of how this reader satisfies its contract; the
/// abstraction itself is independent of traces and could be swapped to
/// a precomputed topology table without touching callers.
/// </summary>
public sealed class EfCoreServiceMapReader : IServiceMapReader
{
    private static readonly MethodInfo JsonAttributeValueMethod =
        typeof(TelemetryDbFunctions).GetMethod(nameof(TelemetryDbFunctions.JsonAttributeValue))!;

    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly IOptionsMonitor<ServiceMapOptions> _serviceMapOptions;

    public EfCoreServiceMapReader(
        IDbContextFactory<TelemetryDbContext> contextFactory,
        IOptionsMonitor<ServiceMapOptions> serviceMapOptions)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(serviceMapOptions);
        _contextFactory = contextFactory;
        _serviceMapOptions = serviceMapOptions;
    }

    public async Task<ServiceMapResult> GetServiceMapAsync(
        ServiceMapQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var fromNano = UnixNanoTime.ToUnixNanoseconds(query.From);
        var toNano = UnixNanoTime.ToUnixNanoseconds(query.To);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Nodes: per-service span counts inside the window. Counts every
        // span (root + children), not only entrypoints — a service called
        // from multiple places gets credit for every call.
        var nodeQuery =
            from s in context.Spans.AsNoTracking()
            where s.StartUnixNano >= fromNano && s.StartUnixNano < toNano
            join r in context.Resources.AsNoTracking() on s.ResourceHash equals r.Hash
            where r.ServiceName != null
            group s.StatusCode by r.ServiceName! into g
            select new
            {
                Service = g.Key,
                Count = (long)g.Count(),
                ErrorCount = (long)g.Count(x => x == SpanStatusCode.Error)
            };

        var serviceNodes = (await nodeQuery.ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(x => new ServiceMapNode(x.Service, ServiceMapNodeKind.Service, x.Count, x.ErrorCount))
            .ToList();

        // Edges: self-join on (TraceId, ParentSpanId == SpanId). Self-
        // loops (parent and child on the same service) are dropped —
        // they're inherent to traces of one service and add only visual
        // noise to the graph. Both sides are filtered by the window so
        // the planner can use the StartUnixNano index on each. Grouping
        // by resource pair and naming services afterwards keeps the
        // resources table out of the join, where PostgreSQL otherwise
        // picks a nested loop over every joined row.
        var edgeRows = await (
                from child in context.Spans.AsNoTracking()
                where child.StartUnixNano >= fromNano && child.StartUnixNano < toNano
                   && child.ParentSpanId != null
                from parent in context.Spans.AsNoTracking()
                where parent.TraceId == child.TraceId
                   && parent.SpanId == child.ParentSpanId
                   && parent.StartUnixNano >= fromNano && parent.StartUnixNano < toNano
                   && parent.ResourceHash != child.ResourceHash
                group child.StatusCode by new { From = parent.ResourceHash, To = child.ResourceHash } into g
                select new
                {
                    g.Key.From,
                    g.Key.To,
                    Count = (long)g.Count(),
                    ErrorCount = (long)g.Count(x => x == SpanStatusCode.Error),
                })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var edgeServices = await LoadServiceNamesAsync(
                context, edgeRows.SelectMany(r => new[] { r.From, r.To }), cancellationToken)
            .ConfigureAwait(false);
        var serviceEdges = edgeRows
            .Select(r => (From: edgeServices.GetValueOrDefault(r.From), To: edgeServices.GetValueOrDefault(r.To), r.Count, r.ErrorCount))
            .Where(e => e.From is not null && e.To is not null && e.From != e.To)
            .GroupBy(e => (From: e.From!, To: e.To!))
            .Select(g => new ServiceMapEdge(g.Key.From, g.Key.To, g.Sum(x => x.Count), g.Sum(x => x.ErrorCount)))
            .ToList();

        // External-dependency synthesis: a remote endpoint that doesn't
        // run an OTel SDK never emits spans of its own. The
        // instrumentation libs inside the *calling* service produce
        // kind=Client spans tagged with attributes that name (or type)
        // the remote — `db.system`, `messaging.system`, `rpc.system`,
        // `peer.service`, `service.peer.name`. We synthesise one
        // virtual node per distinct value and one edge per
        // (host, value) pair — same shape as what Datadog/Honeycomb
        // call "dependencies".
        //
        // Per-span priority: <c>DependencyAttributes</c> is a priority
        // list. For each Client span the reader credits *exactly one*
        // attribute, the first non-null in priority order (COALESCE for
        // the value, CASE for which key matched), in a single pass over
        // the window. This stops a single span from producing two
        // dependency nodes — e.g. the EF Core instrumentation often
        // emits both `db.system=postgresql` and `peer.service=5432`;
        // without the priority we'd render one "postgresql" node *and*
        // one "5432" node for the same call. With the default order
        // (`service.peer.name` first, then the kind discriminators, with
        // `peer.service` last), the kind-named node wins and the port
        // number is suppressed.
        var depKeys = _serviceMapOptions.CurrentValue.DependencyAttributes
            ?.Where(k => !string.IsNullOrEmpty(k))
            ?.Distinct(StringComparer.Ordinal)
            ?.ToArray() ?? [];

        var deps = await LoadDependenciesAsync(context, fromNano, toNano, depKeys, cancellationToken).ConfigureAwait(false);

        // Roll up dependency rows by value into a single node per
        // distinct kind (e.g. one `postgresql` node even when called
        // from multiple host services or matched by multiple keys),
        // then create one edge per (host, value) pair. The chosen
        // <see cref="ServiceMapNode.AttributeKey"/> is the first
        // configured key (in <see cref="ServiceMapOptions.DependencyAttributes"/>
        // order) that contributed any rows to this dep; the SPA
        // uses it to build the drill-down filter.
        var dependencyNodes = deps
            .GroupBy(d => d.DepName, StringComparer.Ordinal)
            .Select(g =>
            {
                var contributingKeys = g.Select(x => x.AttributeKey).ToHashSet(StringComparer.Ordinal);
                var primaryKey = depKeys.FirstOrDefault(k => contributingKeys.Contains(k));
                return new ServiceMapNode(
                    g.Key,
                    ServiceMapNodeKind.Dependency,
                    g.Sum(x => x.Count),
                    g.Sum(x => x.ErrorCount),
                    AttributeKey: primaryKey);
            })
            .ToList();

        var dependencyEdges = deps
            .GroupBy(d => (d.Host, d.DepName))
            .Select(g => new ServiceMapEdge(
                g.Key.Host,
                g.Key.DepName,
                g.Sum(x => x.Count),
                g.Sum(x => x.ErrorCount)))
            .ToList();

        var nodes = serviceNodes.Concat(dependencyNodes).ToList();
        var edges = serviceEdges.Concat(dependencyEdges).ToList();

        // Focus mode: narrow to a service and its direct neighbours.
        // Done in C# (post-fetch) — keeps the SQL queries simple, works
        // on a small result set (services are tens, not millions).
        if (!string.IsNullOrEmpty(query.ServiceName))
        {
            var focus = query.ServiceName;
            var keptEdges = edges.Where(e => e.FromService == focus || e.ToService == focus).ToList();
            var keptServices = new HashSet<string>(StringComparer.Ordinal) { focus };
            foreach (var e in keptEdges)
            {
                keptServices.Add(e.FromService);
                keptServices.Add(e.ToService);
            }
            var keptNodes = nodes.Where(n => keptServices.Contains(n.Service)).ToList();
            return new ServiceMapResult(keptNodes, keptEdges);
        }

        return new ServiceMapResult(nodes, edges);
    }

    private static async Task<List<(string Host, string DepName, string AttributeKey, long Count, long ErrorCount)>> LoadDependenciesAsync(
        TelemetryDbContext context,
        long fromNano,
        long toNano,
        string[] depKeys,
        CancellationToken cancellationToken)
    {
        if (depKeys.Length == 0)
        {
            return [];
        }

        var rows = await context.Spans
            .AsNoTracking()
            .Where(s => s.StartUnixNano >= fromNano && s.StartUnixNano < toNano && s.Kind == SpanKind.Client)
            .Select(DependencyProjection(depKeys))
            .Where(d => d.Value != null)
            .GroupBy(d => new { d.ResourceHash, d.KeyIndex, d.Value })
            .Select(g => new
            {
                g.Key.ResourceHash,
                g.Key.KeyIndex,
                g.Key.Value,
                Count = (long)g.Count(),
                ErrorCount = (long)g.Count(x => x.StatusCode == SpanStatusCode.Error),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var serviceByHash = await LoadServiceNamesAsync(context, rows.Select(r => r.ResourceHash), cancellationToken)
            .ConfigureAwait(false);

        var deps = new List<(string Host, string DepName, string AttributeKey, long Count, long ErrorCount)>(rows.Count);
        foreach (var row in rows)
        {
            if (serviceByHash.TryGetValue(row.ResourceHash, out var host))
            {
                deps.Add((host, row.Value!, depKeys[row.KeyIndex], row.Count, row.ErrorCount));
            }
        }
        return deps;
    }

    private static async Task<Dictionary<byte[], string>> LoadServiceNamesAsync(
        TelemetryDbContext context,
        IEnumerable<byte[]> resourceHashes,
        CancellationToken cancellationToken)
    {
        var hashes = resourceHashes.Distinct(ByteArrayEqualityComparer.Instance).ToArray();
        return await context.Resources
            .AsNoTracking()
            .Where(r => hashes.Contains(r.Hash) && r.ServiceName != null)
            .Select(r => new { r.Hash, r.ServiceName })
            .ToDictionaryAsync(x => x.Hash, x => x.ServiceName!, ByteArrayEqualityComparer.Instance, cancellationToken)
            .ConfigureAwait(false);
    }

    private static Expression<Func<Span, DependencyRow>> DependencyProjection(string[] depKeys)
    {
        var span = Expression.Parameter(typeof(Span), "s");
        var attributes = Expression.Call(
            typeof(EF), nameof(EF.Property), [typeof(string)], span, Expression.Constant(nameof(Span.Attributes)));
        var noValue = Expression.Constant(null, typeof(string));

        Expression value = noValue;
        Expression keyIndex = Expression.Constant(-1);
        for (var i = depKeys.Length - 1; i >= 0; i--)
        {
            var extract = Expression.Call(JsonAttributeValueMethod, attributes, Expression.Constant(depKeys[i]));
            value = Expression.Coalesce(extract, value);
            keyIndex = Expression.Condition(Expression.NotEqual(extract, noValue), Expression.Constant(i), keyIndex);
        }

        var body = Expression.MemberInit(
            Expression.New(typeof(DependencyRow)),
            Expression.Bind(typeof(DependencyRow).GetProperty(nameof(DependencyRow.ResourceHash))!, Expression.Property(span, nameof(Span.ResourceHash))),
            Expression.Bind(typeof(DependencyRow).GetProperty(nameof(DependencyRow.StatusCode))!, Expression.Property(span, nameof(Span.StatusCode))),
            Expression.Bind(typeof(DependencyRow).GetProperty(nameof(DependencyRow.KeyIndex))!, keyIndex),
            Expression.Bind(typeof(DependencyRow).GetProperty(nameof(DependencyRow.Value))!, value));
        return Expression.Lambda<Func<Span, DependencyRow>>(body, span);
    }

    private sealed class DependencyRow
    {
        public byte[] ResourceHash { get; init; } = [];
        public SpanStatusCode StatusCode { get; init; }
        public int KeyIndex { get; init; }
        public string? Value { get; init; }
    }
}
