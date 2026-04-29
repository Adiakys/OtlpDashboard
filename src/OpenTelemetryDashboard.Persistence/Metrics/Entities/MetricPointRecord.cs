using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Metrics.Entities;

/// <summary>
/// Persistence-internal projection of a metric data point: the fact table of
/// the metric star schema. References its <see cref="InstrumentRecord"/> via
/// <see cref="InstrumentId"/>.
/// </summary>
public sealed class MetricPointRecord
{
    public long Id { get; set; }
    public long InstrumentId { get; set; }

    public long TimeUnixNano { get; set; }
    public long StartTimeUnixNano { get; set; }
    public double Value { get; set; }

    public IReadOnlyDictionary<string, object?> Attributes { get; set; } = AttributeMap.Empty;
}
