namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Single metric time-series: the instrument metadata plus its ring-buffer
/// snapshot (optionally filtered by time window). Not paginated because
/// the in-memory ring buffer is already bounded by configuration.
/// </summary>
public sealed record MetricSeriesDto(
    InstrumentDto Instrument,
    IReadOnlyList<MetricPointDto> Points);
