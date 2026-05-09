using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetryDashboard.Persistence.HealthChecks;
using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.UnitTests.Persistence;

public class TelemetrySinkHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Healthy_when_no_drop_observed()
    {
        var clock = new FixedClock(Now);
        var metrics = new TelemetrySinkMetrics(clock);
        var check = new TelemetrySinkHealthCheck(metrics, clock);

        metrics.RecordTraceSuccess(100);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        result.Data["traces.persisted"].ShouldBe(100L);
        result.Data.ContainsKey("lastDropAt").ShouldBeFalse();
    }

    [Fact]
    public async Task Degraded_when_drop_within_window()
    {
        var clock = new FixedClock(Now);
        var metrics = new TelemetrySinkMetrics(clock);
        var check = new TelemetrySinkHealthCheck(metrics, clock);

        metrics.RecordLogDrop(7);
        // Health check fires 1 minute after the drop — well within the 5-minute window.
        clock.Advance(TimeSpan.FromMinutes(1));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Degraded);
        result.Data["logs.dropped"].ShouldBe(7L);
        result.Data.ContainsKey("lastDropAt").ShouldBeTrue();
    }

    [Fact]
    public async Task Healthy_again_once_drop_falls_outside_window()
    {
        var clock = new FixedClock(Now);
        var metrics = new TelemetrySinkMetrics(clock);
        var check = new TelemetrySinkHealthCheck(metrics, clock);

        metrics.RecordMetricDrop(3);
        clock.Advance(TimeSpan.FromMinutes(10));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(HealthStatus.Healthy);
        // Counter remains visible for diagnostics even when no longer degrading.
        result.Data["metrics.dropped"].ShouldBe(3L);
        result.Data.ContainsKey("lastDropAt").ShouldBeTrue();
    }

    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
