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

    private async Task DispatchAsync(IReadOnlyList<TelemetryBatch> batches, CancellationToken cancellationToken)
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

        if (traceBatches is { Count: > 0 })
        {
            await _traceSink.WriteAsync(traceBatches, cancellationToken).ConfigureAwait(false);
        }
        if (logBatches is { Count: > 0 })
        {
            await _logSink.WriteAsync(logBatches, cancellationToken).ConfigureAwait(false);
        }
        if (metricBatches is { Count: > 0 })
        {
            await _metricSink.WriteAsync(metricBatches, cancellationToken).ConfigureAwait(false);
        }
    }
}

internal static partial class TelemetryWriterLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "TelemetryWriter drain timed out after {Seconds}s; in-flight batches may be lost")]
    public static partial void DrainTimedOut(this ILogger logger, int seconds);
}
