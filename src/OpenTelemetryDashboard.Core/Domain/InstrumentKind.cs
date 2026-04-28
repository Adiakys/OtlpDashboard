namespace OpenTelemetryDashboard.Core.Domain;

public enum InstrumentKind
{
    Unspecified = 0,
    Gauge = 1,
    Sum = 2,
    Histogram = 3,
    ExponentialHistogram = 4,
    Summary = 5,
}
