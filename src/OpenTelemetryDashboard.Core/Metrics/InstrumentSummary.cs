using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Metrics;

/// <summary>
/// Read-side projection used by <c>ListInstrumentsAsync</c>: identity
/// (<see cref="InstrumentKey"/>), instrument metadata, the recorded point
/// count for the instrument, and the originating service name (resolved via
/// the resource hash). Mirrors the join the <c>MetricsEndpoints</c> needs to
/// avoid an N+1 round-trip per instrument.
/// </summary>
public sealed record InstrumentSummary(
    InstrumentKey Key,
    Instrument Instrument,
    int PointCount,
    string? ServiceName);
