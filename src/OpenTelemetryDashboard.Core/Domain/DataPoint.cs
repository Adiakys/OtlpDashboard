namespace OpenTelemetryDashboard.Core.Domain;

public sealed class DataPoint
{
    public long StartTimeUnixNano { get; init; }
    public long TimeUnixNano { get; init; }
    public double Value { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = AttributeMap.Empty;
}
