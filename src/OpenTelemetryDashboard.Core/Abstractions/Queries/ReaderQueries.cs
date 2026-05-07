using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Abstractions.Queries;

/// <summary>
/// Keyset-pagination position. <see cref="Time"/> is the primary sort key
/// (<c>TimeUnixNano</c> for logs, <c>StartUnixNano</c> for traces) of the last
/// row returned. <see cref="SecondaryKey"/> is a storage-provided long that
/// disambiguates rows sharing the same <see cref="Time"/>; callers treat it
/// as opaque.
/// </summary>
public readonly record struct CursorPosition(long Time, long SecondaryKey);

/// <summary>
/// Parameters for a time-windowed, keyset-paginated log query. All fields
/// are pre-validated by the caller: <c>From &lt; To</c>, <c>Limit &gt; 0</c>,
/// both timestamps UTC. When <see cref="TraceId"/> is set, results are
/// restricted to log records correlated to that trace. <see cref="MinSeverityNumber"/>
/// is inclusive — only records with <c>SeverityNumber &gt;= MinSeverityNumber</c>
/// pass the filter. <c>0</c> (or <c>null</c>) disables the filter; the
/// indexed column makes higher cutoffs cheap on large windows.
/// <see cref="SeverityNumbersIn"/> is an optional inclusion list intersected
/// with <see cref="MinSeverityNumber"/> — useful for the SPA's multi-bucket
/// picker which can't be expressed as a single <c>&gt;=</c> cutoff.
/// <see cref="BodyContains"/> applies a case-insensitive substring match
/// against the log body.
/// </summary>
public sealed record LogQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    CursorPosition? After,
    TraceId? TraceId = null,
    string? ServiceName = null,
    int? MinSeverityNumber = null,
    IReadOnlyList<int>? SeverityNumbersIn = null,
    string? BodyContains = null,
    IReadOnlyList<AttributeFilter>? AttributeFilters = null);

/// <summary>
/// Parameters for a time-windowed, keyset-paginated trace-summary query.
/// Pre-validation contract matches <see cref="LogQuery"/>.
/// <see cref="StatusFilter"/> narrows by per-trace status using "any-span"
/// semantics (matches the existing <see cref="ServiceName"/> behaviour):
/// <c>Error</c> = the trace contains at least one Error span; <c>Ok</c> = no
/// Error spans. <see cref="MinDurationMs"/>/<see cref="MaxDurationMs"/> are
/// inclusive bounds applied to the trace's wall-clock duration
/// (<c>max(end) - min(start)</c>) after grouping. <see cref="SpanNameContains"/>
/// is "any-span" substring matching against span names.
/// </summary>
public sealed record TraceQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    CursorPosition? After,
    string? ServiceName = null,
    /// <summary>When true, narrow to traces touching at least one
    /// span whose Resource has no `service.name` (null OR empty
    /// string). Mutually exclusive with <see cref="ServiceName"/>;
    /// when both are set, this flag wins. Lets the service-map UI
    /// drill into the "(unnamed)" node — there's no string identity
    /// to pass through <c>service=...</c> for it.</summary>
    bool MatchUnnamedService = false,
    TraceStatusFilter? StatusFilter = null,
    double? MinDurationMs = null,
    double? MaxDurationMs = null,
    string? SpanNameContains = null,
    IReadOnlyList<AttributeFilter>? AttributeFilters = null);

/// <summary>
/// Single key/value pair to require on a span's or log's attribute map.
/// Multi-filter queries AND the pairs together. Match semantics: the pair
/// is matched as a JSON string-typed property (<c>"key":"value"</c>);
/// numeric/boolean attributes aren't filterable in this version. Both
/// fields are pre-validated by the caller — non-empty, no LIKE wildcards.
/// </summary>
public sealed record AttributeFilter(string Key, string Value);

/// <summary>
/// Trace-level status filter, evaluated with "any-span" semantics so it
/// composes with the existing service filter.
/// </summary>
public enum TraceStatusFilter
{
    /// <summary>The trace contains no Error span.</summary>
    Ok,
    /// <summary>The trace contains at least one Error span.</summary>
    Error,
}

/// <summary>
/// "Top-N" aggregation over root spans inside a time window. Group key
/// is the root span name; the four metrics (<c>count</c>, <c>errorCount</c>,
/// <c>avgMs</c>, <c>maxMs</c>) are always computed — the consumer picks
/// which one drives the sort. Attribute filters apply to the root span,
/// so a query like "top-10 GET /counter where http.status_code=500"
/// is well-defined.
/// </summary>
public sealed record TraceAggregationQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    TraceAggregationMetric SortBy,
    string? ServiceName = null,
    IReadOnlyList<AttributeFilter>? AttributeFilters = null);

/// <summary>
/// Sort dimensions exposed by the aggregation endpoint. Higher value =
/// higher rank in all four cases.
/// </summary>
public enum TraceAggregationMetric
{
    /// <summary>Number of root spans that match.</summary>
    Count,
    /// <summary>errorCount / count, in the [0,1] range.</summary>
    ErrorRate,
    /// <summary>Mean of <c>EndUnixNano - StartUnixNano</c> per group, in ms.</summary>
    AvgMs,
    /// <summary>Largest single duration in the group, in ms.</summary>
    MaxMs,
}

/// <summary>
/// One row of <see cref="TraceAggregationQuery"/>'s output. The Reader
/// fills all four metrics; the API surfaces them all so the SPA can
/// render the unsorted ones as secondary columns without a re-fetch
/// when the user re-sorts client-side.
/// </summary>
public sealed record TraceAggregationRow(
    string Key,
    long Count,
    long ErrorCount,
    double AvgMs,
    double MaxMs);

/// <summary>
/// Service-map aggregation: distinct services touched in the window
/// and the cross-service call edges between them. <see cref="ServiceName"/>
/// (when set) narrows the result to that service and its direct neighbours
/// — useful for "focus on this service" mode without changing endpoint.
/// </summary>
public sealed record ServiceMapQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    string? ServiceName = null);

/// <summary>
/// One service in the map. <see cref="Kind"/> distinguishes:
///  - <see cref="ServiceMapNodeKind.Service"/> — an OTel-emitting
///    service (has its own resource and `service.name`).
///  - <see cref="ServiceMapNodeKind.Dependency"/> — a synthesised
///    external entity (e.g. <c>postgresql</c>, <c>billing</c>) inferred
///    from <c>kind=Client</c> spans with the OTel "peer service"
///    attribute (<c>peer.service</c> / <c>service.peer.name</c>) on
///    the host service. Same look-and-feel data shape, but the host
///    service is implicit (the parent end of the edges that point
///    to it).
/// Counts include every span (or every client call for dependencies).
///
/// <see cref="AttributeKey"/> is set only on dependency nodes: the
/// configured attribute (e.g. <c>peer.service</c>) whose value
/// produced this node. It lets the UI build a precise drill-down
/// filter into /traces (search for spans where
/// <c>AttributeKey = Service</c>). When more than one configured
/// key contributed, the first one in configuration order wins —
/// picking just one keeps the UI's "view traces" link a single
/// click rather than a disambiguation.
/// </summary>
public sealed record ServiceMapNode(
    string Service,
    ServiceMapNodeKind Kind,
    long RequestCount,
    long ErrorCount,
    string? AttributeKey = null);

public enum ServiceMapNodeKind
{
    Service,
    Dependency,
}

/// <summary>
/// One directed edge: a span of <see cref="ToService"/> whose parent
/// span belongs to <see cref="FromService"/>. Self-loops
/// (<c>From == To</c>) are filtered out — the SQL self-join rejects
/// them at source.
/// </summary>
public sealed record ServiceMapEdge(
    string FromService,
    string ToService,
    long CallCount,
    long ErrorCount);

public sealed record ServiceMapResult(
    IReadOnlyList<ServiceMapNode> Nodes,
    IReadOnlyList<ServiceMapEdge> Edges);
