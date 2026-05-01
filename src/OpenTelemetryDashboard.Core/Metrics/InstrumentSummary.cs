using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Metrics;

/// <summary>
/// Read-side projection used by <c>ListInstrumentsAsync</c>: identity
/// (<see cref="InstrumentKey"/>), instrument metadata, the recorded point
/// count for the instrument, and the originating service name +
/// `service.instance.id` (resolved via the resource hash). The instance
/// id discriminates two instruments that share name+scope but live on
/// different resources — typical for collector receivers that scrape
/// multiple databases / hosts under the same logical service name.
/// </summary>
public sealed record InstrumentSummary(
    InstrumentKey Key,
    Instrument Instrument,
    int PointCount,
    string? ServiceName,
    string? ServiceInstanceId);
