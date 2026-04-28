namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record SpanLinkDto(
    string TraceId,
    string SpanId,
    IReadOnlyDictionary<string, object?> Attributes);
