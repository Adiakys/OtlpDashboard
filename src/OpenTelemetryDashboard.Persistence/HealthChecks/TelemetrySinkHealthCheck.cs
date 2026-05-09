using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.Persistence.HealthChecks;

/// <summary>
/// Health check that flags the dashboard as <see cref="HealthStatus.Degraded"/>
/// when the EF telemetry sinks have dropped any batch in the recent window.
/// "Healthy" doesn't promise zero loss — only that no loss has been observed
/// in the last <see cref="DegradedWindow"/>. The detail dictionary surfaces
/// per-signal counters so SREs can see <em>what</em> is failing without
/// scraping logs.
/// </summary>
public sealed class TelemetrySinkHealthCheck : IHealthCheck
{
    /// <summary>How recently a drop has to have happened for the check to degrade.</summary>
    public static readonly TimeSpan DegradedWindow = TimeSpan.FromMinutes(5);

    private readonly TelemetrySinkMetrics _metrics;
    private readonly TimeProvider _time;

    public TelemetrySinkHealthCheck(TelemetrySinkMetrics metrics, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(time);
        _metrics = metrics;
        _time = time;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _metrics.Snapshot();
        var data = new Dictionary<string, object>
        {
            ["traces.persisted"] = snapshot.TracesPersisted,
            ["traces.dropped"] = snapshot.TracesDropped,
            ["logs.persisted"] = snapshot.LogsPersisted,
            ["logs.dropped"] = snapshot.LogsDropped,
            ["metrics.persisted"] = snapshot.MetricsPersisted,
            ["metrics.dropped"] = snapshot.MetricsDropped,
        };

        if (snapshot.LastDropAt is { } when)
        {
            data["lastDropAt"] = when.ToString("O");
            var age = _time.GetUtcNow() - when;
            if (age <= DegradedWindow)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Telemetry sink dropped batches in the last {(int)DegradedWindow.TotalMinutes} minutes.",
                    data: data));
            }
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "No telemetry batches dropped in the recent window.",
            data: data));
    }
}
