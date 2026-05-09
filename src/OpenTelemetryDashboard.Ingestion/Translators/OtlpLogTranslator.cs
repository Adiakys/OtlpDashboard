using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using ProtoLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using ResourceProto = OpenTelemetry.Proto.Resource.V1;

namespace OpenTelemetryDashboard.Ingestion.Translators;

public sealed class OtlpLogTranslator
{
    private readonly ILogger<OtlpLogTranslator> _logger;

    public OtlpLogTranslator(ILogger<OtlpLogTranslator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public LogBatch? Translate(ExportLogsServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ResourceLogs.Count == 0)
        {
            return null;
        }

        var resources = new List<Resource>(capacity: request.ResourceLogs.Count);
        var records = new List<Core.Domain.LogRecord>();

        foreach (var resourceLogs in request.ResourceLogs)
        {
            var resource = BuildResource(
                resourceLogs.Resource ?? new ResourceProto.Resource(),
                resourceLogs.SchemaUrl);
            resources.Add(resource);

            foreach (var scopeLogs in resourceLogs.ScopeLogs)
            {
                var scopeName = scopeLogs.Scope?.Name;
                var scopeVersion = scopeLogs.Scope?.Version;

                foreach (var protoLog in scopeLogs.LogRecords)
                {
                    var record = TryBuildLog(protoLog, resource.Hash, scopeName, scopeVersion);
                    if (record is not null)
                    {
                        records.Add(record);
                    }
                }
            }
        }

        if (records.Count == 0 && resources.Count == 0)
        {
            return null;
        }

        return new LogBatch(resources, records);
    }

    private static Resource BuildResource(ResourceProto.Resource protoResource, string schemaUrl)
    {
        var attributes = OtlpConversion.ToAttributeMap(protoResource.Attributes);
        var serviceName = OtlpConversion.ExtractStringAttribute(attributes, "service.name");
        var serviceInstanceId = OtlpConversion.ExtractStringAttribute(attributes, "service.instance.id");
        var normalizedSchemaUrl = string.IsNullOrEmpty(schemaUrl) ? null : schemaUrl;

        var hash = ResourceHasher.Compute(
            serviceName,
            serviceInstanceId,
            normalizedSchemaUrl,
            protoResource.DroppedAttributesCount,
            attributes);

        return new Resource
        {
            Hash = hash,
            ServiceName = serviceName,
            ServiceInstanceId = serviceInstanceId,
            SchemaUrl = normalizedSchemaUrl,
            DroppedAttributesCount = protoResource.DroppedAttributesCount,
            Attributes = attributes,
        };
    }

    private Core.Domain.LogRecord? TryBuildLog(
        ProtoLogRecord protoLog,
        byte[] resourceHash,
        string? scopeName,
        string? scopeVersion)
    {
        var traceId = TraceId.Empty;
        if (protoLog.TraceId.Length == TraceId.SizeInBytes)
        {
            traceId = TraceId.FromBytes(protoLog.TraceId.Span);
        }
        else if (protoLog.TraceId.Length != 0)
        {
            _logger.LogRejected("invalid_trace_id_length");
            return null;
        }

        var spanId = SpanId.Empty;
        if (protoLog.SpanId.Length == SpanId.SizeInBytes)
        {
            spanId = SpanId.FromBytes(protoLog.SpanId.Span);
        }
        else if (protoLog.SpanId.Length != 0)
        {
            _logger.LogRejected("invalid_span_id_length");
            return null;
        }

        return new Core.Domain.LogRecord
        {
            ResourceHash = resourceHash,
            TimeUnixNano = (long)protoLog.TimeUnixNano,
            ObservedTimeUnixNano = (long)protoLog.ObservedTimeUnixNano,
            SeverityNumber = (Core.Domain.SeverityNumber)(int)protoLog.SeverityNumber,
            SeverityText = string.IsNullOrEmpty(protoLog.SeverityText) ? null : protoLog.SeverityText,
            Body = ExtractBody(protoLog),
            TraceId = traceId,
            SpanId = spanId,
            Flags = protoLog.Flags,
            ScopeName = scopeName,
            ScopeVersion = scopeVersion,
            Attributes = OtlpConversion.ToAttributeMap(protoLog.Attributes),
            DroppedAttributesCount = protoLog.DroppedAttributesCount,
        };
    }

    private static string? ExtractBody(ProtoLogRecord protoLog)
    {
        var obj = OtlpConversion.ToObject(protoLog.Body);
        var serialised = obj switch
        {
            null => null,
            string s => s,
            bool b => b ? "true" : "false",
            _ => System.Text.Json.JsonSerializer.Serialize(obj),
        };
        if (serialised is { Length: > OtlpTranslationLimits.MaxLogBodyLength })
        {
            // Storage column has its own length cap, but truncating in
            // the translator (a) bounds the in-memory representation
            // before the EF batch buffers it and (b) leaves the trailing
            // suffix visible so an SRE notices the cut.
            return string.Concat(
                serialised.AsSpan(0, OtlpTranslationLimits.MaxLogBodyLength),
                OtlpTranslationLimits.TruncationSuffix);
        }
        return serialised;
    }
}

internal static partial class OtlpLogTranslatorLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "OTLP log record rejected: {Reason}")]
    public static partial void LogRejected(this ILogger logger, string reason);
}
