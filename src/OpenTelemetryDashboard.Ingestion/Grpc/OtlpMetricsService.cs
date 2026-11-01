using System.Diagnostics;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Grpc;

public sealed class OtlpMetricsService : MetricsService.MetricsServiceBase
{
    private readonly OtlpMetricTranslator _translator;
    private readonly TelemetryChannel _channel;

    public OtlpMetricsService(
        OtlpMetricTranslator translator,
        TelemetryChannel channel)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(channel);

        _translator = translator;
        _channel = channel;
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

        batch = batch with { IngestActivityContext = Activity.Current?.Context ?? default };

        if (!_channel.TryWrite(batch))
        {
            throw new RpcException(new Status(
                StatusCode.ResourceExhausted,
                "Ingestion queue is full; retry after backoff."));
        }

        return Task.FromResult(new ExportMetricsServiceResponse());
    }
}
