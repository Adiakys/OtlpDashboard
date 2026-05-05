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
    string? BodyContains = null);

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
    TraceStatusFilter? StatusFilter = null,
    double? MinDurationMs = null,
    double? MaxDurationMs = null,
    string? SpanNameContains = null);

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
