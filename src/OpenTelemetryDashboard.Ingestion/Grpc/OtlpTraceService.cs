using System.Diagnostics;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Grpc;

public sealed class OtlpTraceService : TraceService.TraceServiceBase
{
    private readonly OtlpTraceTranslator _translator;
    private readonly TelemetryChannel _channel;

    public OtlpTraceService(
        OtlpTraceTranslator translator,
        TelemetryChannel channel)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(channel);

        _translator = translator;
        _channel = channel;
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
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "Ingestion queue is full; retry after backoff."));
        }

        return Task.FromResult(new ExportTraceServiceResponse());
    }
}
