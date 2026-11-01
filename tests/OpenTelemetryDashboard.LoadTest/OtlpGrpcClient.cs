using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace OpenTelemetryDashboard.LoadTest;

/// <summary>
/// Minimal OTLP/gRPC exporter. The Ingestion assembly only generates server
/// stubs, so the unary methods are declared by hand against its message types.
/// </summary>
internal sealed class OtlpGrpcClient : IDisposable
{
    private static readonly Method<ExportTraceServiceRequest, ExportTraceServiceResponse> TraceExport =
        Unary<ExportTraceServiceRequest, ExportTraceServiceResponse>("opentelemetry.proto.collector.trace.v1.TraceService", ExportTraceServiceResponse.Parser);

    private static readonly Method<ExportLogsServiceRequest, ExportLogsServiceResponse> LogsExport =
        Unary<ExportLogsServiceRequest, ExportLogsServiceResponse>("opentelemetry.proto.collector.logs.v1.LogsService", ExportLogsServiceResponse.Parser);

    private static readonly Method<ExportMetricsServiceRequest, ExportMetricsServiceResponse> MetricsExport =
        Unary<ExportMetricsServiceRequest, ExportMetricsServiceResponse>("opentelemetry.proto.collector.metrics.v1.MetricsService", ExportMetricsServiceResponse.Parser);

    private readonly GrpcChannel _channel;
    private readonly CallInvoker _invoker;
    private readonly Metadata _headers = [];

    public OtlpGrpcClient(Uri endpoint, string? apiKey)
    {
        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true },
            MaxSendMessageSize = 16 * 1024 * 1024,
        });
        _invoker = _channel.CreateCallInvoker();
        if (!string.IsNullOrEmpty(apiKey))
        {
            _headers.Add("authorization", $"Bearer {apiKey}");
        }
    }

    public Task ExportAsync(ExportTraceServiceRequest request, CancellationToken ct) =>
        _invoker.AsyncUnaryCall(TraceExport, null, new CallOptions(_headers, cancellationToken: ct), request).ResponseAsync;

    public Task ExportAsync(ExportLogsServiceRequest request, CancellationToken ct) =>
        _invoker.AsyncUnaryCall(LogsExport, null, new CallOptions(_headers, cancellationToken: ct), request).ResponseAsync;

    public Task ExportAsync(ExportMetricsServiceRequest request, CancellationToken ct) =>
        _invoker.AsyncUnaryCall(MetricsExport, null, new CallOptions(_headers, cancellationToken: ct), request).ResponseAsync;

    public void Dispose() => _channel.Dispose();

    private static Method<TRequest, TResponse> Unary<TRequest, TResponse>(string service, MessageParser<TResponse> parser)
        where TRequest : class, IMessage<TRequest>
        where TResponse : class, IMessage<TResponse> =>
        new(
            MethodType.Unary,
            service,
            "Export",
            Marshallers.Create<TRequest>(r => r.ToByteArray(), _ => throw new NotSupportedException()),
            Marshallers.Create(r => r.ToByteArray(), parser.ParseFrom));
}
