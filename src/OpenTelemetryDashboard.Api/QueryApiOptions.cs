using System.ComponentModel.DataAnnotations;

namespace OpenTelemetryDashboard.Api;

/// <summary>
/// Query-API runtime tuning. Bound at startup from
/// <see cref="SectionName"/> and validated via data annotations.
/// </summary>
public sealed class QueryApiOptions
{
    public const string SectionName = "Dashboard:QueryApi";

    [Range(1, 10_000)]
    public int DefaultLimit { get; set; } = 100;

    [Range(1, 10_000)]
    public int MaxLimit { get; set; } = 1_000;

    [Range(1, 24 * 30)]
    public int MaxWindowHours { get; set; } = 24 * 7;
}
