namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record SpanEventDto(
    string Name,
    DateTimeOffset Time,
    IReadOnlyDictionary<string, object?> Attributes);
