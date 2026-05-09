namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Single metric time-series: the instrument metadata plus the points
/// matching the requested time window. Not paginated. <see cref="Truncated"/>
/// is true when the server hit the configured row cap before reaching the
/// end of the window — the caller should narrow the window or back off
/// before refetching.
/// </summary>
public sealed record MetricSeriesDto(
    InstrumentDto Instrument,
    IReadOnlyList<MetricPointDto> Points,
    bool Truncated);
