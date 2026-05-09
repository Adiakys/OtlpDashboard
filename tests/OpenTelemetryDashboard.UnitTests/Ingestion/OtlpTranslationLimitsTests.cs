using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetryDashboard.Ingestion.Translators;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;

namespace OpenTelemetryDashboard.UnitTests.Ingestion;

public class OtlpTranslationLimitsTests
{
    [Fact]
    public void ToObject_returns_null_when_kvlist_nesting_exceeds_max_depth()
    {
        // Build (MaxAttributeDepth + 2) layers of nested kvlists. Anything
        // past MaxAttributeDepth must be refused, not just truncated — a
        // deep recursive descent is exactly the stack-overflow vector
        // the cap exists to block.
        var inner = new AnyValue { StringValue = "leaf" };
        for (var i = 0; i < OtlpTranslationLimits.MaxAttributeDepth + 2; i++)
        {
            var kvlist = new KeyValueList();
            kvlist.Values.Add(new KeyValue { Key = "next", Value = inner });
            inner = new AnyValue { KvlistValue = kvlist };
        }

        var result = OtlpConversion.ToObject(inner);

        // Walk the surviving levels — the descent must terminate with a
        // null somewhere before the leaf.
        object? current = result;
        var levels = 0;
        while (current is IReadOnlyDictionary<string, object?> map && map.TryGetValue("next", out var next))
        {
            current = next;
            levels++;
            if (levels > OtlpTranslationLimits.MaxAttributeDepth + 5) break;
        }
        levels.ShouldBeLessThanOrEqualTo(OtlpTranslationLimits.MaxAttributeDepth);
        // The bottom of the surviving chain is null because the deeper
        // value was refused.
        current.ShouldBeNull();
    }

    [Fact]
    public void ToAttributeMap_caps_attribute_count_per_entity()
    {
        var attrs = new List<KeyValue>();
        for (var i = 0; i < OtlpTranslationLimits.MaxAttributesPerEntity + 50; i++)
        {
            attrs.Add(new KeyValue { Key = $"k{i}", Value = new AnyValue { IntValue = i } });
        }

        var result = OtlpConversion.ToAttributeMap(attrs);

        result.Count.ShouldBe(OtlpTranslationLimits.MaxAttributesPerEntity);
    }

    [Fact]
    public void ToObject_caps_array_collection_size()
    {
        var array = new ArrayValue();
        for (var i = 0; i < OtlpTranslationLimits.MaxAttributeCollectionSize + 50; i++)
        {
            array.Values.Add(new AnyValue { IntValue = i });
        }

        var result = (object?[]?)OtlpConversion.ToObject(new AnyValue { ArrayValue = array });

        result.ShouldNotBeNull();
        result.Length.ShouldBe(OtlpTranslationLimits.MaxAttributeCollectionSize);
    }

    [Fact]
    public void ToObject_truncates_oversized_string_attribute()
    {
        // 2× the cap so we exercise the slice and the suffix concat.
        var huge = new string('x', OtlpTranslationLimits.MaxAttributeStringLength * 2);

        var result = (string?)OtlpConversion.ToObject(new AnyValue { StringValue = huge });

        result.ShouldNotBeNull();
        result.Length.ShouldBeGreaterThan(OtlpTranslationLimits.MaxAttributeStringLength);
        result.Length.ShouldBeLessThan(huge.Length);
        result.ShouldEndWith(OtlpTranslationLimits.TruncationSuffix);
    }

    [Fact]
    public void TraceTranslator_caps_events_and_increments_dropped_counter()
    {
        var translator = new OtlpTraceTranslator(NullLogger<OtlpTraceTranslator>.Instance);
        var protoSpan = NewProtoSpan();
        for (var i = 0; i < OtlpTranslationLimits.MaxEventsPerSpan + 7; i++)
        {
            protoSpan.Events.Add(new OtlpSpan.Types.Event
            {
                Name = $"event-{i}",
                TimeUnixNano = (ulong)i,
            });
        }

        var batch = TranslateSingleSpan(translator, protoSpan);
        batch.ShouldNotBeNull();
        var span = batch!.Spans[0];

        span.Events.Count.ShouldBe(OtlpTranslationLimits.MaxEventsPerSpan);
        span.DroppedEventsCount.ShouldBe(7u);
    }

    [Fact]
    public void TraceTranslator_caps_links_and_increments_dropped_counter()
    {
        var translator = new OtlpTraceTranslator(NullLogger<OtlpTraceTranslator>.Instance);
        var protoSpan = NewProtoSpan();
        for (var i = 0; i < OtlpTranslationLimits.MaxLinksPerSpan + 5; i++)
        {
            protoSpan.Links.Add(new OtlpSpan.Types.Link
            {
                TraceId = ByteString.CopyFrom(new byte[16]),
                SpanId = ByteString.CopyFrom(new byte[8]),
            });
        }

        var batch = TranslateSingleSpan(translator, protoSpan);
        batch.ShouldNotBeNull();
        var span = batch!.Spans[0];

        span.Links.Count.ShouldBe(OtlpTranslationLimits.MaxLinksPerSpan);
        span.DroppedLinksCount.ShouldBe(5u);
    }

    [Fact]
    public void LogTranslator_truncates_oversized_body()
    {
        var translator = new OtlpLogTranslator(NullLogger<OtlpLogTranslator>.Instance);
        var huge = new string('y', OtlpTranslationLimits.MaxLogBodyLength * 2);
        var request = new OpenTelemetry.Proto.Collector.Logs.V1.ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "svc" } },
                },
            },
        };
        var scope = new ScopeLogs { Scope = new InstrumentationScope { Name = "tests" } };
        scope.LogRecords.Add(new LogRecord
        {
            TimeUnixNano = 1,
            Body = new AnyValue { StringValue = huge },
        });
        resourceLogs.ScopeLogs.Add(scope);
        request.ResourceLogs.Add(resourceLogs);

        var batch = translator.Translate(request);
        batch.ShouldNotBeNull();
        var record = batch!.Records[0];

        record.Body.ShouldNotBeNull();
        record.Body!.Length.ShouldBeGreaterThan(OtlpTranslationLimits.MaxLogBodyLength);
        record.Body.Length.ShouldBeLessThan(huge.Length);
        record.Body.ShouldEndWith(OtlpTranslationLimits.TruncationSuffix);
    }

    private static OtlpSpan NewProtoSpan() => new()
    {
        Name = "op",
        Kind = OtlpSpan.Types.SpanKind.Server,
        TraceId = ByteString.CopyFrom(new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }),
        SpanId = ByteString.CopyFrom(new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 }),
        StartTimeUnixNano = 1,
        EndTimeUnixNano = 2,
    };

    private static OpenTelemetryDashboard.Core.Ingestion.TraceBatch? TranslateSingleSpan(
        OtlpTraceTranslator translator,
        OtlpSpan protoSpan)
    {
        var request = new OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceRequest();
        var resourceSpans = new ResourceSpans
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "svc" } },
                },
            },
        };
        var scope = new ScopeSpans { Scope = new InstrumentationScope { Name = "tests" } };
        scope.Spans.Add(protoSpan);
        resourceSpans.ScopeSpans.Add(scope);
        request.ResourceSpans.Add(resourceSpans);

        return translator.Translate(request);
    }
}
