using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using ResourceProto = OpenTelemetry.Proto.Resource.V1;

namespace OpenTelemetryDashboard.Ingestion.Translators;

public sealed class OtlpTraceTranslator
{
    private readonly ILogger<OtlpTraceTranslator> _logger;

    public OtlpTraceTranslator(ILogger<OtlpTraceTranslator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public TraceBatch? Translate(ExportTraceServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ResourceSpans.Count == 0)
        {
            return null;
        }

        var resources = new List<Resource>(capacity: request.ResourceSpans.Count);
        var spans = new List<Core.Domain.Span>();

        foreach (var resourceSpans in request.ResourceSpans)
        {
            var schemaUrl = resourceSpans.SchemaUrl;
            var resource = BuildResource(
                resourceSpans.Resource ?? new ResourceProto.Resource(),
                schemaUrl);
            resources.Add(resource);

            foreach (var scopeSpans in resourceSpans.ScopeSpans)
            {
                var scopeName = scopeSpans.Scope?.Name;
                var scopeVersion = scopeSpans.Scope?.Version;

                foreach (var protoSpan in scopeSpans.Spans)
                {
                    var span = TryBuildSpan(protoSpan, resource.Hash, scopeName, scopeVersion);
                    if (span is not null)
                    {
                        spans.Add(span);
                    }
                }
            }
        }

        if (spans.Count == 0 && resources.Count == 0)
        {
            return null;
        }

        return new TraceBatch(resources, spans);
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

    private Core.Domain.Span? TryBuildSpan(
        OpenTelemetry.Proto.Trace.V1.Span protoSpan,
        byte[] resourceHash,
        string? scopeName,
        string? scopeVersion)
    {
        if (protoSpan.TraceId.Length != TraceId.SizeInBytes)
        {
            _logger.SpanRejected("invalid_trace_id_length");
            return null;
        }

        if (protoSpan.SpanId.Length != SpanId.SizeInBytes)
        {
            _logger.SpanRejected("invalid_span_id_length");
            return null;
        }

        var traceId = TraceId.FromBytes(protoSpan.TraceId.Span);
        var spanId = SpanId.FromBytes(protoSpan.SpanId.Span);

        if (traceId.IsEmpty)
        {
            _logger.SpanRejected("zero_trace_id");
            return null;
        }

        if (spanId.IsEmpty)
        {
            _logger.SpanRejected("zero_span_id");
            return null;
        }

        if (protoSpan.EndTimeUnixNano != 0 &&
            protoSpan.StartTimeUnixNano != 0 &&
            protoSpan.EndTimeUnixNano < protoSpan.StartTimeUnixNano)
        {
            _logger.SpanRejected("end_before_start");
            return null;
        }

        SpanId? parentSpanId = null;
        if (protoSpan.ParentSpanId.Length == SpanId.SizeInBytes)
        {
            var parent = SpanId.FromBytes(protoSpan.ParentSpanId.Span);
            if (!parent.IsEmpty)
            {
                parentSpanId = parent;
            }
        }

        // Hard caps on events/links protect against pathological spans
        // (instrumentation loops, retry storms): every entry above the
        // limit is counted into DroppedEventsCount / DroppedLinksCount on
        // top of what the producer itself reported as dropped, so the
        // ingest counter survives the truncation. We build the lists
        // first because Span exposes the counts as init-only.
        var (events, droppedEvents) = TranslateEvents(protoSpan);
        var (links, droppedLinks) = TranslateLinks(protoSpan);

        return new Core.Domain.Span
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            ResourceHash = resourceHash,
            Name = protoSpan.Name,
            Kind = (Core.Domain.SpanKind)(int)protoSpan.Kind,
            StartUnixNano = (long)protoSpan.StartTimeUnixNano,
            EndUnixNano = (long)protoSpan.EndTimeUnixNano,
            StatusCode = protoSpan.Status is not null
                ? (SpanStatusCode)(int)protoSpan.Status.Code
                : SpanStatusCode.Unset,
            StatusMessage = protoSpan.Status?.Message,
            ScopeName = scopeName,
            ScopeVersion = scopeVersion,
            Flags = protoSpan.Flags,
            Attributes = OtlpConversion.ToAttributeMap(protoSpan.Attributes),
            Events = events,
            Links = links,
            DroppedAttributesCount = protoSpan.DroppedAttributesCount,
            DroppedEventsCount = protoSpan.DroppedEventsCount + droppedEvents,
            DroppedLinksCount = protoSpan.DroppedLinksCount + droppedLinks,
        };
    }

    private static (List<SpanEvent> Events, uint Dropped) TranslateEvents(OpenTelemetry.Proto.Trace.V1.Span protoSpan)
    {
        var events = new List<SpanEvent>(Math.Min(protoSpan.Events.Count, OtlpTranslationLimits.MaxEventsPerSpan));
        var dropped = 0u;
        foreach (var protoEvent in protoSpan.Events)
        {
            if (events.Count >= OtlpTranslationLimits.MaxEventsPerSpan)
            {
                dropped++;
                continue;
            }
            events.Add(new SpanEvent
            {
                Name = protoEvent.Name,
                TimeUnixNano = (long)protoEvent.TimeUnixNano,
                Attributes = OtlpConversion.ToAttributeMap(protoEvent.Attributes),
                DroppedAttributesCount = protoEvent.DroppedAttributesCount,
            });
        }
        return (events, dropped);
    }

    private static (List<SpanLink> Links, uint Dropped) TranslateLinks(OpenTelemetry.Proto.Trace.V1.Span protoSpan)
    {
        var links = new List<SpanLink>(Math.Min(protoSpan.Links.Count, OtlpTranslationLimits.MaxLinksPerSpan));
        var dropped = 0u;
        foreach (var protoLink in protoSpan.Links)
        {
            if (protoLink.TraceId.Length != TraceId.SizeInBytes ||
                protoLink.SpanId.Length != SpanId.SizeInBytes)
            {
                continue;
            }
            if (links.Count >= OtlpTranslationLimits.MaxLinksPerSpan)
            {
                dropped++;
                continue;
            }
            links.Add(new SpanLink
            {
                TraceId = TraceId.FromBytes(protoLink.TraceId.Span),
                SpanId = SpanId.FromBytes(protoLink.SpanId.Span),
                Flags = protoLink.Flags,
                Attributes = OtlpConversion.ToAttributeMap(protoLink.Attributes),
                DroppedAttributesCount = protoLink.DroppedAttributesCount,
            });
        }
        return (links, dropped);
    }
}

internal static partial class OtlpTraceTranslatorLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "OTLP span rejected: {Reason}")]
    public static partial void SpanRejected(this ILogger logger, string reason);
}
