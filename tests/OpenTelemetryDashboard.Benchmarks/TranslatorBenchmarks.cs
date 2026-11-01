using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;
using Google.Protobuf;

namespace OpenTelemetryDashboard.Benchmarks;

/// <summary>
/// CPU and allocation cost of the ingest hot path before the channel:
/// protobuf decode plus translation to domain batches.
/// </summary>
[MemoryDiagnoser]
public class TranslatorBenchmarks
{
    private readonly OtlpTraceTranslator _traceTranslator = new(NullLogger<OtlpTraceTranslator>.Instance);
    private readonly OtlpLogTranslator _logTranslator = new(NullLogger<OtlpLogTranslator>.Instance);
    private readonly OtlpMetricTranslator _metricTranslator = new(NullLogger<OtlpMetricTranslator>.Instance);

    private byte[] _traces = [];
    private byte[] _logs = [];
    private byte[] _metrics = [];

    [Params(100, 512)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var factory = new OtlpPayloadFactory(seed: 42, serviceCount: 10);
        var now = DateTimeOffset.UtcNow;
        _traces = factory.CreateTraces(Items, now).ToByteArray();
        _logs = factory.CreateLogs(Items, now).ToByteArray();
        _metrics = factory.CreateMetrics(Items, now).ToByteArray();
    }

    [Benchmark]
    public ExportTraceServiceRequest TracesDecodeOnly() => ExportTraceServiceRequest.Parser.ParseFrom(_traces);

    [Benchmark]
    public TraceBatch? Traces() => _traceTranslator.Translate(ExportTraceServiceRequest.Parser.ParseFrom(_traces));

    [Benchmark]
    public LogBatch? Logs() => _logTranslator.Translate(ExportLogsServiceRequest.Parser.ParseFrom(_logs));

    [Benchmark]
    public MetricBatch? Metrics() => _metricTranslator.Translate(ExportMetricsServiceRequest.Parser.ParseFrom(_metrics));
}
