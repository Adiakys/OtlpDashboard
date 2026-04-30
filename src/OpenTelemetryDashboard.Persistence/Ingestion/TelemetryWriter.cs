using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Persistence.Ingestion;

/// <summary>
/// Reads <see cref="TelemetryBatch"/> items from the <see cref="TelemetryChannel"/>,
/// groups them by concrete type, and dispatches each group to its typed sink
/// (<see cref="ITraceSink"/>, <see cref="ILogSink"/>, <see cref="IMetricSink"/>).
/// Owns no storage knowledge of its own — it is pure plumbing.
/// Graceful shutdown drains remaining items with a configurable deadline.
/// </summary>
public sealed class TelemetryWriter : BackgroundService
{
    /// <summary>
    /// Source for the dispatch span the writer wraps around each batch flush.
    /// Picked up by the self-instrumentation via the <c>OpenTelemetryDashboard.*</c>
    /// glob, so the EF INSERTs run as children of this span.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("OpenTelemetryDashboard.TelemetryWriter");

    private readonly TelemetryChannel _channel;
    private readonly ITraceSink _traceSink;
    private readonly ILogSink _logSink;
    private readonly IMetricSink _metricSink;
    private readonly TelemetryChannelOptions _channelOptions;
    private readonly IngestionShutdownOptions _shutdownOptions;
    private readonly ILogger<TelemetryWriter> _logger;

    public TelemetryWriter(
        TelemetryChannel channel,
        ITraceSink traceSink,
        ILogSink logSink,
        IMetricSink metricSink,
        IOptions<TelemetryChannelOptions> channelOptions,
        IOptions<IngestionShutdownOptions> shutdownOptions,
        ILogger<TelemetryWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(traceSink);
        ArgumentNullException.ThrowIfNull(logSink);
        ArgumentNullException.ThrowIfNull(metricSink);
        ArgumentNullException.ThrowIfNull(channelOptions);
        ArgumentNullException.ThrowIfNull(shutdownOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _channel = channel;
        _traceSink = traceSink;
        _logSink = logSink;
        _metricSink = metricSink;
        _channelOptions = channelOptions.Value;
        _shutdownOptions = shutdownOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batches = new List<TelemetryBatch>(capacity: _channelOptions.MaxBatchSize);
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (batches.Count < _channelOptions.MaxBatchSize &&
                       reader.TryRead(out var batch))
                {
                    batches.Add(batch);
                }

                if (batches.Count > 0)
                {
                    await DispatchAsync(batches, stoppingToken).ConfigureAwait(false);
                    batches.Clear();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown. Remaining items are drained in StopAsync.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Complete();

        using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        drainCts.CancelAfter(TimeSpan.FromSeconds(_shutdownOptions.DrainTimeoutSeconds));

        try
        {
            await base.StopAsync(drainCts.Token).ConfigureAwait(false);
            await DrainAsync(drainCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.DrainTimedOut(_shutdownOptions.DrainTimeoutSeconds);
        }
    }

    private async Task DrainAsync(CancellationToken cancellationToken)
    {
        var batches = new List<TelemetryBatch>(capacity: _channelOptions.MaxBatchSize);
        var reader = _channel.Reader;

        while (reader.TryRead(out var batch))
        {
            batches.Add(batch);
            if (batches.Count >= _channelOptions.MaxBatchSize)
            {
                await DispatchAsync(batches, cancellationToken).ConfigureAwait(false);
                batches.Clear();
            }
        }

        if (batches.Count > 0)
        {
            await DispatchAsync(batches, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(List<TelemetryBatch> batches, CancellationToken cancellationToken)
    {
        List<TraceBatch>? traceBatches = null;
        List<LogBatch>? logBatches = null;
        List<MetricBatch>? metricBatches = null;

        foreach (var batch in batches)
        {
            switch (batch)
            {
                case TraceBatch trace:
                    (traceBatches ??= []).Add(trace);
                    break;
                case LogBatch log:
                    (logBatches ??= []).Add(log);
                    break;
                case MetricBatch metric:
                    (metricBatches ??= []).Add(metric);
                    break;
            }
        }

        // The writer flush runs in a hosted-service loop, detached from the
        // ingest HTTP/gRPC requests. Wrap the dispatch in a fresh root activity
        // and attach span links back to the originating ingest activities, so
        // the EF INSERTs become children of "Dispatch" while the trace listing
        // still surfaces the causality without forcing a parent/child link
        // across the asynchronous channel boundary.
        var links = CollectIngestLinks(batches);
        using var activity = ActivitySource.StartActivity(
            "TelemetryWriter.Dispatch",
            ActivityKind.Internal,
            parentContext: default,
            tags: null,
            links: links);
        activity?.SetTag("dashboard.batch.count", batches.Count);
        activity?.SetTag("dashboard.batch.traces", traceBatches?.Count ?? 0);
        activity?.SetTag("dashboard.batch.logs", logBatches?.Count ?? 0);
        activity?.SetTag("dashboard.batch.metrics", metricBatches?.Count ?? 0);

        // Each sink owns its own DbContext and writes to a disjoint table set
        // (Spans / SpanEvents / SpanLinks vs. Logs vs. Instruments / MetricPoints).
        // No shared change-tracker, no FK overlap on the write path — the
        // resource upsert reads the same Resources row but each sink takes its
        // own snapshot. On Postgres / SqlServer (where IO is the bottleneck)
        // running them concurrently roughly cuts the dispatch latency in
        // proportion to the kinds present in the batch. SQLite serialises
        // writes at the DB level, but the CPU work (tracking, JSON encode)
        // still parallelises.
        var tasks = new List<Task>(3);
        if (traceBatches is { Count: > 0 })
        {
            tasks.Add(_traceSink.WriteAsync(traceBatches, cancellationToken));
        }
        if (logBatches is { Count: > 0 })
        {
            tasks.Add(_logSink.WriteAsync(logBatches, cancellationToken));
        }
        if (metricBatches is { Count: > 0 })
        {
            tasks.Add(_metricSink.WriteAsync(metricBatches, cancellationToken));
        }
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private static List<ActivityLink>? CollectIngestLinks(List<TelemetryBatch> batches)
    {
        List<ActivityLink>? links = null;
        foreach (var batch in batches)
        {
            var ctx = batch.IngestActivityContext;
            if (ctx == default) continue;
            (links ??= new List<ActivityLink>(batches.Count)).Add(new ActivityLink(ctx));
        }
        return links;
    }
}

internal static partial class TelemetryWriterLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "TelemetryWriter drain timed out after {Seconds}s; in-flight batches may be lost")]
    public static partial void DrainTimedOut(this ILogger logger, int seconds);
}
