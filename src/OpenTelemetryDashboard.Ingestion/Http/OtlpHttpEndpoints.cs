using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;

namespace OpenTelemetryDashboard.Ingestion.Http;

public static class OtlpHttpEndpoints
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string GoogleProtobufContentType = "application/vnd.google.protobuf";

    public static RouteGroupBuilder MapOtlpHttpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/v1");
        group.MapPost("/traces", HandleTracesAsync).WithName("OtlpHttpTraces");
        group.MapPost("/logs", HandleLogsAsync).WithName("OtlpHttpLogs");
        group.MapPost("/metrics", HandleMetricsAsync).WithName("OtlpHttpMetrics");

        return group;
    }

    private static async Task<IResult> HandleTracesAsync(
        HttpContext context,
        OtlpTraceTranslator translator,
        TelemetryChannel channel,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedContentType(context.Request.ContentType))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportTraceServiceRequest request;
        try
        {
            request = await ParseAsync(context.Request.Body, ExportTraceServiceRequest.Parser, cancellationToken);
        }
        catch (InvalidProtocolBufferException)
        {
            return Results.BadRequest();
        }

        var batch = translator.Translate(request);
        if (batch is null || batch.Spans.Count == 0)
        {
            return ProtobufResult(new ExportTraceServiceResponse());
        }

        if (!channel.TryWrite(batch))
        {
            loggerFactory.CreateLogger("OtlpHttp.Traces").HttpChannelFull();
            context.Response.Headers.RetryAfter = "1";
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return ProtobufResult(new ExportTraceServiceResponse());
    }

    private static async Task<IResult> HandleLogsAsync(
        HttpContext context,
        OtlpLogTranslator translator,
        TelemetryChannel channel,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedContentType(context.Request.ContentType))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportLogsServiceRequest request;
        try
        {
            request = await ParseAsync(context.Request.Body, ExportLogsServiceRequest.Parser, cancellationToken);
        }
        catch (InvalidProtocolBufferException)
        {
            return Results.BadRequest();
        }

        var batch = translator.Translate(request);
        if (batch is null || batch.Records.Count == 0)
        {
            return ProtobufResult(new ExportLogsServiceResponse());
        }

        if (!channel.TryWrite(batch))
        {
            loggerFactory.CreateLogger("OtlpHttp.Logs").HttpChannelFull();
            context.Response.Headers.RetryAfter = "1";
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return ProtobufResult(new ExportLogsServiceResponse());
    }

    private static async Task<IResult> HandleMetricsAsync(
        HttpContext context,
        OtlpMetricTranslator translator,
        TelemetryChannel channel,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsSupportedContentType(context.Request.ContentType))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportMetricsServiceRequest request;
        try
        {
            request = await ParseAsync(context.Request.Body, ExportMetricsServiceRequest.Parser, cancellationToken);
        }
        catch (InvalidProtocolBufferException)
        {
            return Results.BadRequest();
        }

        var batch = translator.Translate(request);
        if (batch is null || batch.Samples.Count == 0)
        {
            return ProtobufResult(new ExportMetricsServiceResponse());
        }

        if (!channel.TryWrite(batch))
        {
            loggerFactory.CreateLogger("OtlpHttp.Metrics").HttpChannelFull();
            context.Response.Headers.RetryAfter = "1";
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return ProtobufResult(new ExportMetricsServiceResponse());
    }

    private static async Task<T> ParseAsync<T>(
        Stream body,
        MessageParser<T> parser,
        CancellationToken cancellationToken)
        where T : IMessage<T>
    {
        await using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return parser.ParseFrom(buffer);
    }

    private static IResult ProtobufResult<T>(T message) where T : IMessage<T>
    {
        var bytes = message.ToByteArray();
        return Results.File(bytes, ProtobufContentType);
    }

    private static bool IsSupportedContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return false;
        }

        // Accept parameters like "application/x-protobuf; charset=utf-8"
        var semicolonIndex = contentType.IndexOf(';', StringComparison.Ordinal);
        var media = semicolonIndex >= 0 ? contentType[..semicolonIndex].Trim() : contentType.Trim();

        return media.Equals(ProtobufContentType, StringComparison.OrdinalIgnoreCase)
            || media.Equals(GoogleProtobufContentType, StringComparison.OrdinalIgnoreCase);
    }
}

internal static partial class OtlpHttpEndpointsLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "HTTP OTLP export rejected: telemetry channel is full")]
    public static partial void HttpChannelFull(this ILogger logger);
}
