namespace OpenTelemetryDashboard.Core.Domain;

public sealed class Span
{
    public required TraceId TraceId { get; init; }
    public required SpanId SpanId { get; init; }
    public required byte[] ResourceHash { get; init; }
    public required string Name { get; init; }

    public SpanId? ParentSpanId { get; init; }
    public SpanKind Kind { get; init; }
    public long StartUnixNano { get; init; }
    public long EndUnixNano { get; init; }
    public SpanStatusCode StatusCode { get; init; }
    public string? StatusMessage { get; init; }
    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }
    public uint Flags { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = AttributeMap.Empty;
    public List<SpanEvent> Events { get; init; } = [];
    public List<SpanLink> Links { get; init; } = [];

    public uint DroppedAttributesCount { get; init; }
    public uint DroppedEventsCount { get; init; }
    public uint DroppedLinksCount { get; init; }
}
