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
/// </summary>
public sealed record LogQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    CursorPosition? After,
    TraceId? TraceId = null,
    string? ServiceName = null,
    int? MinSeverityNumber = null);

/// <summary>
/// Parameters for a time-windowed, keyset-paginated trace-summary query.
/// Pre-validation contract matches <see cref="LogQuery"/>.
/// </summary>
public sealed record TraceQuery(
    DateTimeOffset From,
    DateTimeOffset To,
    int Limit,
    CursorPosition? After,
    string? ServiceName = null);
