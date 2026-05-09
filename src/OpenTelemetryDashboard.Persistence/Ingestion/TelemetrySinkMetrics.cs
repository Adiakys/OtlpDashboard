namespace OpenTelemetryDashboard.Persistence.Ingestion;

/// <summary>
/// Process-wide counters surfacing the EF telemetry sinks' success/drop
/// ratio. Sinks bump the success bucket on every persisted batch and the
/// drop bucket on every batch lost after exhausting retries; the health
/// check (<c>TelemetrySinkHealthCheck</c>) reads the same instance to flag
/// a degraded posture.
/// </summary>
public sealed class TelemetrySinkMetrics
{
    private readonly TimeProvider _time;

    private long _tracesPersisted;
    private long _tracesDropped;
    private long _logsPersisted;
    private long _logsDropped;
    private long _metricsPersisted;
    private long _metricsDropped;
    private long _lastDropTicks;

    public TelemetrySinkMetrics(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    public void RecordTraceSuccess(int spans) => Interlocked.Add(ref _tracesPersisted, spans);

    public void RecordTraceDrop(int spans)
    {
        Interlocked.Add(ref _tracesDropped, spans);
        MarkLastDrop();
    }

    public void RecordLogSuccess(int logs) => Interlocked.Add(ref _logsPersisted, logs);

    public void RecordLogDrop(int logs)
    {
        Interlocked.Add(ref _logsDropped, logs);
        MarkLastDrop();
    }

    public void RecordMetricSuccess(int points) => Interlocked.Add(ref _metricsPersisted, points);

    public void RecordMetricDrop(int points)
    {
        Interlocked.Add(ref _metricsDropped, points);
        MarkLastDrop();
    }

    public TelemetrySinkSnapshot Snapshot()
    {
        var ticks = Interlocked.Read(ref _lastDropTicks);
        return new TelemetrySinkSnapshot(
            TracesPersisted: Interlocked.Read(ref _tracesPersisted),
            TracesDropped: Interlocked.Read(ref _tracesDropped),
            LogsPersisted: Interlocked.Read(ref _logsPersisted),
            LogsDropped: Interlocked.Read(ref _logsDropped),
            MetricsPersisted: Interlocked.Read(ref _metricsPersisted),
            MetricsDropped: Interlocked.Read(ref _metricsDropped),
            LastDropAt: ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero));
    }

    private void MarkLastDrop() =>
        Interlocked.Exchange(ref _lastDropTicks, _time.GetUtcNow().UtcTicks);
}

public sealed record TelemetrySinkSnapshot(
    long TracesPersisted,
    long TracesDropped,
    long LogsPersisted,
    long LogsDropped,
    long MetricsPersisted,
    long MetricsDropped,
    DateTimeOffset? LastDropAt);
