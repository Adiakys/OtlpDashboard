using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Api.Mappings;

/// <summary>
/// Domain → DTO projections for the metric endpoints:
/// <see cref="Instrument"/> (+ <see cref="InstrumentKey"/> identity and the
/// total point count) → <see cref="InstrumentDto"/>, and
/// <see cref="DataPoint"/> → <see cref="MetricPointDto"/>. Pure mapping.
/// </summary>
internal static class MetricMappings
{
    public static InstrumentDto ToDto(
        this Instrument instrument,
        InstrumentKey key,
        int pointCount,
        string? serviceName,
        string? serviceInstanceId)
    {
        ArgumentNullException.ThrowIfNull(instrument);

        return new InstrumentDto(
            ResourceHash: key.ResourceHashHex,
            ServiceName: serviceName,
            ServiceInstanceId: serviceInstanceId,
            ScopeName: key.ScopeName,
            Name: instrument.Name,
            Kind: instrument.Kind.ToString(),
            Description: instrument.Description,
            Unit: instrument.Unit,
            IsMonotonic: instrument.IsMonotonic,
            Temporality: instrument.Temporality.ToString(),
            PointCount: pointCount);
    }

    public static MetricPointDto ToDto(this DataPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        return new MetricPointDto(
            Time: UnixNanoTime.FromUnixNanoseconds(point.TimeUnixNano),
            StartTime: UnixNanoTime.FromUnixNanoseconds(point.StartTimeUnixNano),
            Value: point.Value,
            Attributes: point.Attributes);
    }
}
