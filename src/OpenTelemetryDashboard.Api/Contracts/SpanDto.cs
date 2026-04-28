namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record SpanDto(
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    DateTimeOffset Start,
    DateTimeOffset End,
    double DurationMs,
    string StatusCode,
    string? StatusMessage,
    string? ScopeName,
    string? ScopeVersion,
    /// <summary>Resource `service.name` of this span — per-span because a trace may span multiple apps.</summary>
    string? ServiceName,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyList<SpanEventDto> Events,
    IReadOnlyList<SpanLinkDto> Links);
