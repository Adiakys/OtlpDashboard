namespace OpenTelemetryDashboard.Core.Domain;

public sealed class Instrument
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public InstrumentKind Kind { get; init; }
    public bool IsMonotonic { get; init; }
    public AggregationTemporality Temporality { get; init; }
}

public enum AggregationTemporality
{
    Unspecified = 0,
    Delta = 1,
    Cumulative = 2,
}
