using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Metrics.Entities;

/// <summary>
/// Persistence-internal projection of a metric instrument: the dimension table
/// of the metric star schema. One row per <see cref="ResourceHash"/> +
/// <see cref="ScopeName"/> + <see cref="Name"/> + <see cref="Kind"/>.
/// Kept separate from the Core <see cref="Instrument"/> domain type so the
/// foreign-key relationship to <see cref="MetricPointRecord"/> can carry a
/// surrogate <see cref="Id"/> without polluting Core.
/// </summary>
public sealed class InstrumentRecord
{
    public long Id { get; set; }

    public byte[] ResourceHash { get; set; } = [];
    public string ScopeName { get; set; } = string.Empty;
    public string? ScopeVersion { get; set; }

    public string Name { get; set; } = string.Empty;
    public InstrumentKind Kind { get; set; }
    public string? Description { get; set; }
    public string? Unit { get; set; }
    public bool IsMonotonic { get; set; }
    public AggregationTemporality Temporality { get; set; }
}
