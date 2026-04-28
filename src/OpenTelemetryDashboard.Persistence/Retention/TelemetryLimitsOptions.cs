using System.ComponentModel.DataAnnotations;

namespace OpenTelemetryDashboard.Persistence.Retention;

/// <summary>
/// Retention windows for each telemetry kind, enforced periodically by the
/// <see cref="TelemetryRetentionHost"/>. A value of <c>0</c> disables retention
/// for that kind (records are kept indefinitely).
/// <para>
/// Bound from the <c>Dashboard:TelemetryLimits</c> section — environment
/// variables <c>DASHBOARD__TELEMETRYLIMITS__MAXLOGDAYS</c>,
/// <c>DASHBOARD__TELEMETRYLIMITS__MAXTRACEDAYS</c>,
/// <c>DASHBOARD__TELEMETRYLIMITS__MAXMETRICDAYS</c>.
/// </para>
/// </summary>
public sealed class TelemetryLimitsOptions
{
    public const string SectionName = "Dashboard:TelemetryLimits";

    [Range(0.0, 3650.0)]
    public double MaxLogDays { get; set; }

    [Range(0.0, 3650.0)]
    public double MaxTraceDays { get; set; }

    [Range(0.0, 3650.0)]
    public double MaxMetricDays { get; set; }

    /// <summary>How often the retention sweep runs, in minutes. Default 60.</summary>
    [Range(1, 1440)]
    public int SweepIntervalMinutes { get; set; } = 60;
}
