namespace OpenTelemetryDashboard.Core.Domain;

public static class AttributeMap
{
    public static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(capacity: 0);
}
