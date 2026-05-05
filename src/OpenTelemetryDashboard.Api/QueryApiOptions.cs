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
    public int DefaultLimit { get; init; } = 1_000;

    [Range(1, 25_000)]
    public int MaxLimit { get; init; } = 10_000;

    [Range(1, 24 * 90)]
    public int MaxWindowHours { get; init; } = 24 * 7;
}
