using System.ComponentModel.DataAnnotations;

namespace OpenTelemetryDashboard.Core.Ingestion;

public sealed class TelemetryChannelOptions
{
    public const string SectionName = "OpenTelemetryDashboard:Ingestion:Channel";

    [Range(1, int.MaxValue)]
    public int Capacity { get; set; } = 10_000;

    [Range(1, 10_000)]
    public int MaxBatchSize { get; set; } = 512;

    [Range(10, 60_000)]
    public int FlushIntervalMs { get; set; } = 250;
}

public sealed class IngestionShutdownOptions
{
    public const string SectionName = "OpenTelemetryDashboard:Ingestion:Shutdown";

    [Range(1, 600)]
    public int DrainTimeoutSeconds { get; set; } = 30;
}
