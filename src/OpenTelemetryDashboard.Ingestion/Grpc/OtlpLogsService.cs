using System.Diagnostics;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Grpc;

public sealed class OtlpLogsService : LogsService.LogsServiceBase
{
    private readonly OtlpLogTranslator _translator;
    private readonly TelemetryChannel _channel;

    public OtlpLogsService(
        OtlpLogTranslator translator,
        TelemetryChannel channel)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(channel);

        _translator = translator;
        _channel = channel;
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
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "Ingestion queue is full; retry after backoff."));
        }

        return Task.FromResult(new ExportLogsServiceResponse());
    }
}
