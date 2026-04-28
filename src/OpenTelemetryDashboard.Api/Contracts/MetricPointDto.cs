namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record MetricPointDto(
    DateTimeOffset Time,
    DateTimeOffset StartTime,
    double Value,
    IReadOnlyDictionary<string, object?> Attributes);
