namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record TraceDetailDto(
    string TraceId,
    IReadOnlyList<SpanDto> Spans);
