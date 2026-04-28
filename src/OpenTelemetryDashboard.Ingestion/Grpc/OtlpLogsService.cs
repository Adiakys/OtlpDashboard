using System.Diagnostics;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Grpc;

public sealed class OtlpLogsService : LogsService.LogsServiceBase
{
    private readonly OtlpLogTranslator _translator;
    private readonly TelemetryChannel _channel;
    private readonly ILogger<OtlpLogsService> _logger;

    public OtlpLogsService(
        OtlpLogTranslator translator,
        TelemetryChannel channel,
        ILogger<OtlpLogsService> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _channel = channel;
        _logger = logger;
    }

    public override Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var batch = _translator.Translate(request);
        if (batch is null || batch.Records.Count == 0)
        {
            return Task.FromResult(new ExportLogsServiceResponse());
        }

        batch = batch with { IngestActivityContext = Activity.Current?.Context ?? default };

        if (!_channel.TryWrite(batch))
        {
            _logger.LogsChannelFull();
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "Ingestion queue is full; retry after backoff."));
        }

        return Task.FromResult(new ExportLogsServiceResponse());
    }
}

internal static partial class OtlpLogsServiceLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "gRPC log export rejected: telemetry channel is full")]
    public static partial void LogsChannelFull(this ILogger logger);
}
