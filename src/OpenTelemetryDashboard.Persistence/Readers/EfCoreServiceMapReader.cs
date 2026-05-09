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
        // loops (parent and child on the same service) are dropped at
        // SQL — they're inherent to traces of one service and add only
        // visual noise to the graph. Both sides are filtered by the
        // window so the planner can use the StartUnixNano index on each.
        var edgeQuery =
            from child in context.Spans.AsNoTracking()
            where child.StartUnixNano >= fromNano && child.StartUnixNano < toNano
               && child.ParentSpanId != null
            from parent in context.Spans.AsNoTracking()
            where parent.TraceId == child.TraceId
               && parent.SpanId == child.ParentSpanId
               && parent.StartUnixNano >= fromNano && parent.StartUnixNano < toNano
            join cr in context.Resources.AsNoTracking() on child.ResourceHash equals cr.Hash
            join pr in context.Resources.AsNoTracking() on parent.ResourceHash equals pr.Hash
            where pr.ServiceName != null && cr.ServiceName != null
               && pr.ServiceName != cr.ServiceName
            group child.StatusCode by new { From = pr.ServiceName!, To = cr.ServiceName! } into g
            select new ServiceMapEdge(
                g.Key.From,
                g.Key.To,
                g.Count(),
                g.Count(x => x == SpanStatusCode.Error));

        var serviceEdges = await edgeQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

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
        // attribute, the first non-null in priority order. Lower-
        // priority queries explicitly filter out spans that have any
        // higher-priority key set. This stops a single span from
        // producing two dependency nodes — e.g. the EF Core
        // instrumentation often emits both `db.system=postgresql` and
        // `peer.service=5432`; without the priority filter we'd render
        // one "postgresql" node *and* one "5432" node for the same
        // call. With the default order (`service.peer.name` first,
        // then the kind discriminators, with `peer.service` last),
        // the kind-named node wins and the port number is suppressed.
        var depKeys = _serviceMapOptions.CurrentValue.DependencyAttributes
            ?.Where(k => !string.IsNullOrEmpty(k))
            ?.Distinct(StringComparer.Ordinal)
            ?.ToArray() ?? [];

        var deps = new List<(string Host, string DepName, string AttributeKey, long Count, long ErrorCount)>();
        for (var i = 0; i < depKeys.Length; i++)
        {
            // Closure-captured `key` is lifted by EF as a parameter,
            // so each iteration emits its own parameterised SQL plan.
            var attributeKey = depKeys[i];
            var higherPriorityKeys = i == 0 ? Array.Empty<string>() : depKeys[..i];

            // Base set: Client spans in the window with a resolvable
            // host service name.
            IQueryable<Span> spans = context.Spans.AsNoTracking()
                .Where(s => s.StartUnixNano >= fromNano && s.StartUnixNano < toNano)
                .Where(s => s.Kind == SpanKind.Client);

            // Cumulative IS NULL exclusions for every higher-priority
            // key. Each Where() folds into the SQL WHERE chain as an
            // additional AND clause, parameterised on the key string —
            // so SQLite / PG / SQL Server all see the same shape.
            foreach (var higherKey in higherPriorityKeys)
            {
                var captured = higherKey;
                spans = spans.Where(s =>
                    TelemetryDbFunctions.JsonAttributeValue(
                        EF.Property<string>(s, nameof(Span.Attributes)),
                        captured) == null);
            }

            var depQuery =
                from s in spans
                join r in context.Resources.AsNoTracking() on s.ResourceHash equals r.Hash
                where r.ServiceName != null
                let depValue = TelemetryDbFunctions.JsonAttributeValue(
                    EF.Property<string>(s, nameof(Span.Attributes)),
                    attributeKey)
                where depValue != null
                group s.StatusCode by new { Host = r.ServiceName!, DepValue = depValue! } into g
                select new
                {
                    g.Key.Host,
                    g.Key.DepValue,
                    Count = (long)g.Count(),
                    ErrorCount = (long)g.Count(x => x == SpanStatusCode.Error)
                };

            var rows = await depQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var r in rows)
            {
                deps.Add((r.Host, r.DepValue, attributeKey, r.Count, r.ErrorCount));
            }
        }

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
}
