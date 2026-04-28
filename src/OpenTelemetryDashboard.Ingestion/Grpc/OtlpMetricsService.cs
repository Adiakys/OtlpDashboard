using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Grpc;

public sealed class OtlpMetricsService : MetricsService.MetricsServiceBase
{
    private readonly OtlpMetricTranslator _translator;
    private readonly TelemetryChannel _channel;
    private readonly ILogger<OtlpMetricsService> _logger;

    public OtlpMetricsService(
        OtlpMetricTranslator translator,
        TelemetryChannel channel,
        ILogger<OtlpMetricsService> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _channel = channel;
        _logger = logger;
    }

    public override Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var batch = _translator.Translate(request);
        if (batch is null || batch.Samples.Count == 0)
        {
            return Task.FromResult(new ExportMetricsServiceResponse());
        }

        if (!_channel.TryWrite(batch))
        {
            _logger.MetricsChannelFull();
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "Ingestion queue is full; retry after backoff."));
        }

        return Task.FromResult(new ExportMetricsServiceResponse());
    }
}

internal static partial class OtlpMetricsServiceLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "gRPC metrics export rejected: telemetry channel is full")]
    public static partial void MetricsChannelFull(this ILogger logger);
}
