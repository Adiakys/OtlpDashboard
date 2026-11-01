using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenTelemetryDashboard.Core.Ingestion;

/// <summary>
/// Bounded in-process queue that decouples OTLP handlers (producers) from the
/// background writer that persists telemetry (single consumer).
/// </summary>
public sealed class TelemetryChannel
{
    private const int RejectionReportSeconds = 10;

    private readonly Channel<TelemetryBatch> _channel;
    private readonly ILogger<TelemetryChannel> _logger;
    private readonly TimeProvider _time;
    private readonly int _maxPendingItems;
    private long _pendingItems;
    private long _rejectedBatches;
    private long _rejectedItems;
    private long _nextRejectionReport;

    public TelemetryChannel(
        IOptions<TelemetryChannelOptions> options,
        ILogger<TelemetryChannel> logger,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);

        _logger = logger;
        _time = time;
        var capacity = options.Value.Capacity;
        _maxPendingItems = options.Value.MaxPendingItems;

        _channel = Channel.CreateBounded<TelemetryBatch>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        _logger.ChannelCreated(capacity);
    }

    public ChannelReader<TelemetryBatch> Reader => _channel.Reader;

    public long PendingItems => Interlocked.Read(ref _pendingItems);

    /// <summary>
    /// Non-blocking enqueue. Returns <c>false</c> if the channel is full, completed,
    /// or already holds <see cref="TelemetryChannelOptions.MaxPendingItems"/> items not
    /// yet released by the writer, letting the caller respond with backpressure
    /// (gRPC ResourceExhausted / HTTP 429). The budget is soft: a batch is admitted
    /// while it is not exhausted, so a single batch larger than the budget never starves.
    /// </summary>
    public bool TryWrite(TelemetryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (Volatile.Read(ref _pendingItems) >= _maxPendingItems || !_channel.Writer.TryWrite(batch))
        {
            RecordRejection(batch.ItemCount);
            return false;
        }

        Interlocked.Add(ref _pendingItems, batch.ItemCount);
        return true;
    }

    /// <summary>
    /// Returns the budget held by batches the writer is done with, persisted or dropped.
    /// </summary>
    public void Release(IReadOnlyList<TelemetryBatch> batches)
    {
        ArgumentNullException.ThrowIfNull(batches);

        long items = 0;
        foreach (var batch in batches)
        {
            items += batch.ItemCount;
        }
        Interlocked.Add(ref _pendingItems, -items);
    }

    // Under sustained overload every export is rejected: one summary per interval instead of one line each.
    private void RecordRejection(int items)
    {
        Interlocked.Increment(ref _rejectedBatches);
        Interlocked.Add(ref _rejectedItems, items);

        var now = _time.GetUtcNow().UtcTicks;
        var due = Interlocked.Read(ref _nextRejectionReport);
        if (now < due)
        {
            return;
        }
        var next = now + RejectionReportSeconds * TimeSpan.TicksPerSecond;
        if (Interlocked.CompareExchange(ref _nextRejectionReport, next, due) != due)
        {
            return;
        }

        _logger.ExportsRejected(
            Interlocked.Exchange(ref _rejectedBatches, 0),
            Interlocked.Exchange(ref _rejectedItems, 0),
            PendingItems,
            RejectionReportSeconds);
    }

    /// <summary>
    /// Signals no more writers. The reader will drain remaining items, then complete.
    /// </summary>
    public bool Complete() => _channel.Writer.TryComplete();
}

internal static partial class TelemetryChannelLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Telemetry channel created with capacity {Capacity}")]
    public static partial void ChannelCreated(this ILogger logger, int capacity);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Rejected {Requests} OTLP export requests ({Items} items): ingestion queue is full with {PendingItems} items pending. Further rejections are summarized every {IntervalSeconds}s")]
    public static partial void ExportsRejected(this ILogger logger, long requests, long items, long pendingItems, int intervalSeconds);
}
