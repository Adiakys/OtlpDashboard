using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Read-side contract for span storage. Implementations MUST use read-only
/// (no-tracking) semantics.
/// </summary>
public interface ITraceReader
{
    Task<Span?> FindSpanAsync(TraceId traceId, SpanId spanId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="maxSpans"/> spans belonging to
    /// <paramref name="traceId"/>, paired with the resource's
    /// <c>service.name</c> (null when unset). A single trace may span
    /// multiple services; each span carries its own value. The cap protects
    /// against pathological traces (instrumentation loops, retry storms);
    /// when it fires <c>Truncated</c> on the snapshot is true.
    /// </summary>
    Task<TraceSpansSnapshot> GetSpansInTraceAsync(
        TraceId traceId,
        int maxSpans,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <c>query.Limit + 1</c> trace summaries within the time
    /// window, ordered by earliest-span <c>StartUnixNano</c> descending. The
    /// extra element lets callers detect the need for a continuation cursor.
    /// The yielded <c>SecondaryKey</c> is a storage-provided long the caller
    /// round-trips via <see cref="CursorPosition"/>. <c>ServiceName</c> is the
    /// root span's resource <c>service.name</c>.
    /// </summary>
    IAsyncEnumerable<(TraceSummary Summary, long SecondaryKey, string? ServiceName)> QueryTraceSummariesAsync(
        TraceQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the distinct, non-null <c>service.name</c> values attached to
    /// traces whose earliest span falls inside the window.
    /// </summary>
    IAsyncEnumerable<string> GetDistinctServiceNamesAsync(
        DateTimeOffset fromTime,
        DateTimeOffset toTime,
        CancellationToken cancellationToken);

    /// <summary>
    /// Top-N root-span aggregation grouped by span name. Ordered by the
    /// query's <see cref="TraceAggregationMetric"/> descending, capped at
    /// <see cref="TraceAggregationQuery.Limit"/>. All four metrics are
    /// always computed so the consumer can render secondary columns
    /// without a refetch on re-sort.
    /// </summary>
    Task<IReadOnlyList<TraceAggregationRow>> AggregateTracesAsync(
        TraceAggregationQuery query,
        CancellationToken cancellationToken);
}
