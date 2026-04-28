namespace OpenTelemetryDashboard.Core.Domain;

public sealed class SpanLink
{
    public required TraceId TraceId { get; init; }
    public required SpanId SpanId { get; init; }
    public uint Flags { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = AttributeMap.Empty;
    public uint DroppedAttributesCount { get; init; }
}
