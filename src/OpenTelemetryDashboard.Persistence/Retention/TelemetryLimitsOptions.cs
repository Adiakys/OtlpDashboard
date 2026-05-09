using System.ComponentModel.DataAnnotations;

namespace OpenTelemetryDashboard.Persistence.Retention;

/// <summary>
/// Retention windows for each telemetry kind, enforced periodically by the
/// <see cref="TelemetryRetentionHost"/>. A value of <c>0</c> disables retention
/// for that kind (records are kept indefinitely) — surfaced as
/// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>
/// by <see cref="HealthChecks.RetentionPostureHealthCheck"/> so SREs see the
/// posture without scraping configuration.
/// <para>
/// Bound from the <c>Dashboard:TelemetryLimits</c> section — environment
/// variables <c>DASHBOARD__TELEMETRYLIMITS__MAXLOGDAYS</c>,
/// <c>DASHBOARD__TELEMETRYLIMITS__MAXTRACEDAYS</c>,
/// <c>DASHBOARD__TELEMETRYLIMITS__MAXMETRICDAYS</c>.
/// </para>
/// <para>
/// Defaults (30 / 7 / 7 days) target a "debug + recent forensics" workload:
/// logs hold a month so an SRE can investigate last-week incidents; traces
/// and metrics rotate weekly because high-cardinality spans and split-by
/// metric attributes inflate fast — the dashboard isn't designed for
/// month-long capacity-planning trends.
/// </para>
/// </summary>
public sealed class TelemetryLimitsOptions
{
    public const string SectionName = "Dashboard:TelemetryLimits";

    [Range(0.0, 3650.0)]
    public double MaxLogDays { get; init; } = 30.0;

    [Range(0.0, 3650.0)]
    public double MaxTraceDays { get; init; } = 7.0;

    [Range(0.0, 3650.0)]
    public double MaxMetricDays { get; init; } = 7.0;

    /// <summary>How often the retention sweep runs, in minutes. Default 60.</summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; init; } = 60;
}
