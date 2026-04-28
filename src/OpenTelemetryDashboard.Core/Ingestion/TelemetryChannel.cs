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
    private readonly Channel<TelemetryBatch> _channel;
    private readonly ILogger<TelemetryChannel> _logger;

    public TelemetryChannel(
        IOptions<TelemetryChannelOptions> options,
        ILogger<TelemetryChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        var capacity = options.Value.Capacity;

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

    /// <summary>
    /// Non-blocking enqueue. Returns <c>false</c> if the channel is full or completed,
    /// letting the caller respond with backpressure (gRPC ResourceExhausted / HTTP 429).
    /// </summary>
    public bool TryWrite(TelemetryBatch batch) => _channel.Writer.TryWrite(batch);

    public ValueTask WriteAsync(TelemetryBatch batch, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(batch, cancellationToken);

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
}
