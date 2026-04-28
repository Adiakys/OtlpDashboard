using System.ComponentModel.DataAnnotations;

namespace OpenTelemetryDashboard.Persistence.Metrics.InMemory;

public sealed class InMemoryMetricStoreOptions
{
    public const string SectionName = "OpenTelemetryDashboard:Metrics:InMemory";

    [Range(1, 1_000_000)]
    public int MaxInstruments { get; set; } = 5_000;

    [Range(1, 100_000)]
    public int MaxPointsPerInstrument { get; set; } = 1_000;
}
