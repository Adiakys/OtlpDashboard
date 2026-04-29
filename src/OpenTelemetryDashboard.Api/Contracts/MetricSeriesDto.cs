namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Single metric time-series: the instrument metadata plus the points
/// matching the optional time-window filter. Not paginated — series are
/// bounded by the metric retention policy.
/// </summary>
public sealed record MetricSeriesDto(
    InstrumentDto Instrument,
    IReadOnlyList<MetricPointDto> Points);
