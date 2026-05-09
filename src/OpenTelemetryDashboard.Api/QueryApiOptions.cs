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

    /// <summary>
    /// Hard cap on the number of <c>MetricPoint</c> rows returned by
    /// <c>GET /v1/metrics/points</c>. Without this an instrument with millions
    /// of points (high-frequency gauge × no retention) would drag the entire
    /// series into the host's memory in a single request. The reader signals
    /// truncation via <c>MetricSeriesDto.Truncated</c>.
    /// </summary>
    [Range(100, 1_000_000)]
    public int MaxMetricPoints { get; init; } = 50_000;

    /// <summary>
    /// Hard cap on the number of spans returned by
    /// <c>GET /v1/traces/{traceId}</c>. Pathological traces (instrumentation
    /// loops, retry storms) can exceed this; the reader truncates and the
    /// response signals it via <c>TraceDetailDto.Truncated</c>.
    /// </summary>
    [Range(100, 100_000)]
    public int MaxSpansPerTrace { get; init; } = 5_000;
}
