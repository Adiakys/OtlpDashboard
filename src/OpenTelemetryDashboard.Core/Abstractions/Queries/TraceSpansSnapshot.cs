using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Abstractions.Queries;

/// <summary>
/// Read-side projection used by <c>GetSpansInTraceAsync</c>: the spans that
/// belong to a trace (paired with the resource's <c>service.name</c>) plus
/// a flag indicating whether the reader hit the configured per-trace cap
/// before returning every span. When <see cref="Truncated"/> is true the
/// caller must surface it to the user — the trace is incomplete, not empty.
/// </summary>
public sealed record TraceSpansSnapshot(
    IReadOnlyList<TraceSpanRow> Spans,
    bool Truncated);

/// <summary>Span + the resolved service name for its resource (null when unset).</summary>
public sealed record TraceSpanRow(Span Span, string? ServiceName);
