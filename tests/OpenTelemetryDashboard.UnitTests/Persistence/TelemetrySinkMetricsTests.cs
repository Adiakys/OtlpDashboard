using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.UnitTests.Persistence;

public class TelemetrySinkMetricsTests
{
    [Fact]
    public void Snapshot_starts_at_zero_with_no_drop_timestamp()
    {
        var metrics = new TelemetrySinkMetrics(TimeProvider.System);

        var snap = metrics.Snapshot();

        snap.TracesPersisted.ShouldBe(0);
        snap.TracesDropped.ShouldBe(0);
        snap.LogsPersisted.ShouldBe(0);
        snap.LogsDropped.ShouldBe(0);
        snap.MetricsPersisted.ShouldBe(0);
        snap.MetricsDropped.ShouldBe(0);
        snap.LastDropAt.ShouldBeNull();
    }

    [Fact]
    public void Records_aggregate_per_signal_independently()
    {
        var metrics = new TelemetrySinkMetrics(TimeProvider.System);

        metrics.RecordTraceSuccess(10);
        metrics.RecordTraceSuccess(5);
        metrics.RecordLogSuccess(3);
        metrics.RecordMetricSuccess(7);

        var snap = metrics.Snapshot();
        snap.TracesPersisted.ShouldBe(15);
        snap.LogsPersisted.ShouldBe(3);
        snap.MetricsPersisted.ShouldBe(7);
    }

    [Fact]
    public void Drop_stamps_LastDropAt_with_the_TimeProvider_clock()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));
        var metrics = new TelemetrySinkMetrics(clock);

        metrics.RecordLogDrop(42);

        var snap = metrics.Snapshot();
        snap.LogsDropped.ShouldBe(42);
        snap.LastDropAt.ShouldBe(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Subsequent_drop_advances_LastDropAt()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));
        var metrics = new TelemetrySinkMetrics(clock);

        metrics.RecordTraceDrop(1);
        clock.Advance(TimeSpan.FromMinutes(3));
        metrics.RecordMetricDrop(2);

        var snap = metrics.Snapshot();
        snap.TracesDropped.ShouldBe(1);
        snap.MetricsDropped.ShouldBe(2);
        snap.LastDropAt.ShouldBe(new DateTimeOffset(2026, 5, 9, 12, 3, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Minimal mutable <see cref="TimeProvider"/> for tests. We don't use
    /// Microsoft.Extensions.Time.Testing.FakeTimeProvider because the unit
    /// test project doesn't otherwise depend on that package.
    /// </summary>
    private sealed class FixedClock : TimeProvider
    {
        private DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
