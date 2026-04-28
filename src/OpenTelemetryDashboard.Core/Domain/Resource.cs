namespace OpenTelemetryDashboard.Core.Domain;

public sealed class Resource
{
    public required byte[] Hash { get; init; }
    public string? ServiceName { get; init; }
    public string? ServiceInstanceId { get; init; }
    public string? SchemaUrl { get; init; }
    public uint DroppedAttributesCount { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = AttributeMap.Empty;
}
