using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Persistence.Retention;

namespace OpenTelemetryDashboard.Persistence.HealthChecks;

/// <summary>
/// Surfaces the retention posture through <c>/healthz</c>: when any of the
/// three windows (logs / traces / metrics) is set to <c>0</c> the affected
/// kind is kept indefinitely, so the dashboard's storage will grow without
/// bound. Returns <see cref="HealthStatus.Degraded"/> in that case so an
/// SRE looking at the orchestrator dashboard sees the posture without
/// having to scrape the configured retention values from logs.
/// </summary>
internal sealed class RetentionPostureHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<TelemetryLimitsOptions> _options;

    public RetentionPostureHealthCheck(IOptionsMonitor<TelemetryLimitsOptions> options)
    {
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var current = _options.CurrentValue;
        var data = new Dictionary<string, object>
        {
            ["maxLogDays"] = current.MaxLogDays,
            ["maxTraceDays"] = current.MaxTraceDays,
            ["maxMetricDays"] = current.MaxMetricDays,
            ["sweepIntervalMinutes"] = current.SweepIntervalMinutes,
        };

        var unbounded = new List<string>(3);
        if (current.MaxLogDays <= 0) unbounded.Add("logs");
        if (current.MaxTraceDays <= 0) unbounded.Add("traces");
        if (current.MaxMetricDays <= 0) unbounded.Add("metrics");

        if (unbounded.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                "All telemetry kinds have a finite retention window.", data: data));
        }

        return Task.FromResult(HealthCheckResult.Degraded(
            $"Retention disabled for: {string.Join(", ", unbounded)} — storage will grow without bound.",
            data: data));
    }
}
