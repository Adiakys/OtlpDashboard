using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Grpc;

public sealed class OtlpTraceService : TraceService.TraceServiceBase
{
    private readonly OtlpTraceTranslator _translator;
    private readonly TelemetryChannel _channel;
    private readonly ILogger<OtlpTraceService> _logger;

    public OtlpTraceService(
        OtlpTraceTranslator translator,
        TelemetryChannel channel,
        ILogger<OtlpTraceService> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _channel = channel;
        _logger = logger;
    }

    public override Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var batch = _translator.Translate(request);
        if (batch is null || batch.Spans.Count == 0)
        {
            return Task.FromResult(new ExportTraceServiceResponse());
        }

        batch = batch with { IngestActivityContext = Activity.Current?.Context ?? default };

        if (!_channel.TryWrite(batch))
        {
            _logger.TraceChannelFull();
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "Ingestion queue is full; retry after backoff."));
        }

        return Task.FromResult(new ExportTraceServiceResponse());
    }
}

internal static partial class OtlpTraceServiceLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "gRPC trace export rejected: telemetry channel is full")]
    public static partial void TraceChannelFull(this ILogger logger);
}
