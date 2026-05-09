namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Full span list for one trace. <see cref="Truncated"/> is true when the
/// trace contained more spans than the per-trace cap and the returned list
/// is an early prefix — the SPA must surface this so users don't read an
/// incomplete trace as complete.
/// </summary>
public sealed record TraceDetailDto(
    string TraceId,
    IReadOnlyList<SpanDto> Spans,
    bool Truncated);
