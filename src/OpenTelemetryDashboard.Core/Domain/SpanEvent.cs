namespace OpenTelemetryDashboard.Core.Domain;

public sealed class SpanEvent
{
    public required string Name { get; init; }
    public long TimeUnixNano { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = AttributeMap.Empty;
    public uint DroppedAttributesCount { get; init; }
}
