using System.Diagnostics;
using Grpc.Core;
using OpenTelemetryDashboard.Benchmarks;

namespace OpenTelemetryDashboard.LoadTest;

/// <summary>
/// Pushes OTLP over gRPC from N workers for a fixed duration, while sampling
/// the dashboard process and probing how long a fresh trace takes to become
/// queryable.
/// </summary>
internal sealed class IngestRunner(LoadOptions options, OtlpGrpcClient client, TraceProbe probe, ProcessSampler? sampler)
{
    private readonly Dictionary<string, SignalCounters> _counters =
        options.Signals.ToDictionary(s => s, _ => new SignalCounters(), StringComparer.OrdinalIgnoreCase);

    public async Task<IngestResult> RunAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(options.DurationSeconds));
        var started = Stopwatch.StartNew();

        var workers = Enumerable.Range(0, options.Workers).Select(i => Task.Run(() => WorkerAsync(i, cts.Token))).ToArray();
        var timelineTask = TimelineAsync(started, cts.Token);
        var freshnessTask = FreshnessAsync(cts.Token);

        await Task.WhenAll(workers);
        var sendSeconds = started.Elapsed.TotalSeconds;
        var timeline = await timelineTask;
        var freshness = await freshnessTask;

        return new IngestResult(
            Math.Round(sendSeconds, 2),
            _counters.ToDictionary(kv => kv.Key, kv => kv.Value.Summarize(sendSeconds)),
            freshness.Summarize(),
            timeline);
    }

    private async Task WorkerAsync(int index, CancellationToken ct)
    {
        var factory = new OtlpPayloadFactory(seed: 1000 + index, options.Services);
        var spread = new Random(2000 + index);
        var interval = options.TargetRequestsPerSecond > 0
            ? TimeSpan.FromSeconds(options.Workers / options.TargetRequestsPerSecond)
            : TimeSpan.Zero;
        var next = Stopwatch.GetTimestamp();
        var turn = index;

        while (!ct.IsCancellationRequested)
        {
            var signal = options.Signals[turn++ % options.Signals.Count];
            var counters = _counters[signal];
            var now = DateTimeOffset.UtcNow.AddMinutes(-spread.NextDouble() * options.SpreadMinutes);
            var started = Stopwatch.GetTimestamp();
            try
            {
                var task = signal switch
                {
                    "traces" => client.ExportAsync(factory.CreateTraces(options.BatchSize, now), ct),
                    "logs" => client.ExportAsync(factory.CreateLogs(options.BatchSize, now), ct),
                    "metrics" => client.ExportAsync(factory.CreateMetrics(options.BatchSize, now), ct),
                    _ => throw new ArgumentException($"Unknown signal '{signal}'."),
                };
                await task;
                counters.Accepted(options.BatchSize, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.ResourceExhausted)
            {
                counters.Rejected();
                await Task.Delay(50, CancellationToken.None);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                counters.Failed(ex);
            }

            if (interval > TimeSpan.Zero)
            {
                next += (long)(interval.TotalSeconds * Stopwatch.Frequency);
                var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), next);
                if (wait > TimeSpan.Zero)
                {
                    try { await Task.Delay(wait, ct); } catch (OperationCanceledException) { break; }
                }
            }
        }
    }

    private async Task<List<TimelinePoint>> TimelineAsync(Stopwatch started, CancellationToken ct)
    {
        var timeline = new List<TimelinePoint>();
        long lastItems = 0;
        long lastRejected = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var items = _counters.Values.Sum(c => c.Items);
                var rejected = _counters.Values.Sum(c => c.RejectedCount);
                var sample = sampler?.Sample();
                timeline.Add(new TimelinePoint(
                    (int)Math.Round(started.Elapsed.TotalSeconds),
                    items - lastItems,
                    rejected - lastRejected,
                    sample?.RssMb,
                    sample?.CpuPercent));
                lastItems = items;
                lastRejected = rejected;
            }
        }
        catch (OperationCanceledException)
        {
        }
        return timeline;
    }

    private async Task<LatencyRecorder> FreshnessAsync(CancellationToken ct)
    {
        var recorder = new LatencyRecorder();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            var visibleAfter = await probe.MeasureAsync(TimeSpan.FromSeconds(30));
            if (visibleAfter is double ms)
            {
                recorder.Record(ms);
            }
        }
        return recorder;
    }
}

internal sealed class SignalCounters
{
    private long _requests;
    private long _items;
    private long _rejected;
    private long _errors;
    private string? _lastError;
    private readonly LatencyRecorder _latency = new();

    public long Items => Interlocked.Read(ref _items);
    public long RejectedCount => Interlocked.Read(ref _rejected);

    public void Accepted(int items, double latencyMs)
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Add(ref _items, items);
        _latency.Record(latencyMs);
    }

    public void Rejected() => Interlocked.Increment(ref _rejected);

    public void Failed(Exception ex)
    {
        Interlocked.Increment(ref _errors);
        _lastError = ex.Message;
    }

    public SignalResult Summarize(double seconds) => new(
        _requests,
        _items,
        Math.Round(_items / seconds, 0),
        _rejected,
        _errors,
        _lastError,
        _latency.Summarize());
}

internal sealed record SignalResult(
    long AcceptedRequests,
    long AcceptedItems,
    double ItemsPerSecond,
    long RejectedRequests,
    long FailedRequests,
    string? LastError,
    LatencySummary ExportLatencyMs);

internal sealed record TimelinePoint(int Second, long Items, long Rejected, double? RssMb, double? CpuPercent);

internal sealed record IngestResult(
    double SendSeconds,
    Dictionary<string, SignalResult> Signals,
    LatencySummary FreshnessMs,
    List<TimelinePoint> Timeline);
