using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Core.Metrics;
using ProtoAggregationTemporality = OpenTelemetry.Proto.Metrics.V1.AggregationTemporality;
using ResourceProto = OpenTelemetry.Proto.Resource.V1;

namespace OpenTelemetryDashboard.Ingestion.Translators;

/// <summary>
/// Translates an <see cref="ExportMetricsServiceRequest"/> into a domain
/// <see cref="MetricBatch"/> suitable for the shared ingestion pipeline.
/// v1 supports Gauge and Sum (NumberDataPoint); Histogram/ExponentialHistogram/
/// Summary are accepted but samples are dropped with a diagnostic log.
/// </summary>
public sealed class OtlpMetricTranslator
{
    private readonly ILogger<OtlpMetricTranslator> _logger;

    public OtlpMetricTranslator(ILogger<OtlpMetricTranslator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public MetricBatch? Translate(ExportMetricsServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ResourceMetrics.Count == 0)
        {
            return null;
        }

        var resources = new List<Resource>(capacity: request.ResourceMetrics.Count);
        var samples = new List<MetricSample>();

        foreach (var resourceMetrics in request.ResourceMetrics)
        {
            var resource = BuildResource(
                resourceMetrics.Resource ?? new ResourceProto.Resource(),
                resourceMetrics.SchemaUrl);
            resources.Add(resource);

            foreach (var scopeMetrics in resourceMetrics.ScopeMetrics)
            {
                var scopeName = scopeMetrics.Scope?.Name ?? string.Empty;
                foreach (var metric in scopeMetrics.Metrics)
                {
                    AppendSamples(samples, resource.Hash, scopeName, metric);
                }
            }
        }

        if (samples.Count == 0 && resources.Count == 0)
        {
            return null;
        }

        return new MetricBatch(resources, samples);
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

    private void AppendSamples(List<MetricSample> samples, byte[] resourceHash, string scopeName, Metric metric)
    {
        switch (metric.DataCase)
        {
            case Metric.DataOneofCase.Gauge:
                AppendNumberSamples(
                    samples,
                    resourceHash,
                    scopeName,
                    metric,
                    InstrumentKind.Gauge,
                    Core.Domain.AggregationTemporality.Unspecified,
                    isMonotonic: false,
                    metric.Gauge.DataPoints);
                break;

            case Metric.DataOneofCase.Sum:
                AppendNumberSamples(
                    samples,
                    resourceHash,
                    scopeName,
                    metric,
                    InstrumentKind.Sum,
                    MapTemporality(metric.Sum.AggregationTemporality),
                    metric.Sum.IsMonotonic,
                    metric.Sum.DataPoints);
                break;

            case Metric.DataOneofCase.Histogram:
            case Metric.DataOneofCase.ExponentialHistogram:
            case Metric.DataOneofCase.Summary:
                _logger.MetricKindDropped(metric.Name, metric.DataCase);
                break;

            default:
                break;
        }
    }

    private static void AppendNumberSamples(
        List<MetricSample> samples,
        byte[] resourceHash,
        string scopeName,
        Metric metric,
        InstrumentKind kind,
        Core.Domain.AggregationTemporality temporality,
        bool isMonotonic,
        RepeatedField<NumberDataPoint> points)
    {
        if (points.Count == 0)
        {
            return;
        }

        var instrument = new Instrument
        {
            Name = metric.Name,
            Description = string.IsNullOrEmpty(metric.Description) ? null : metric.Description,
            Unit = string.IsNullOrEmpty(metric.Unit) ? null : metric.Unit,
            Kind = kind,
            IsMonotonic = isMonotonic,
            Temporality = temporality,
        };

        var key = InstrumentKey.Create(resourceHash, scopeName, metric.Name, kind);

        foreach (var point in points)
        {
            var value = point.ValueCase switch
            {
                NumberDataPoint.ValueOneofCase.AsDouble => point.AsDouble,
                NumberDataPoint.ValueOneofCase.AsInt => (double)point.AsInt,
                _ => double.NaN,
            };

            if (double.IsNaN(value))
            {
                continue;
            }

            var dataPoint = new DataPoint
            {
                StartTimeUnixNano = (long)point.StartTimeUnixNano,
                TimeUnixNano = (long)point.TimeUnixNano,
                Value = value,
                Attributes = OtlpConversion.ToAttributeMap(point.Attributes),
            };

            samples.Add(new MetricSample(key, instrument, dataPoint));
        }
    }

    private static Core.Domain.AggregationTemporality MapTemporality(ProtoAggregationTemporality temporality) =>
        temporality switch
        {
            ProtoAggregationTemporality.Delta => Core.Domain.AggregationTemporality.Delta,
            ProtoAggregationTemporality.Cumulative => Core.Domain.AggregationTemporality.Cumulative,
            _ => Core.Domain.AggregationTemporality.Unspecified,
        };
}

internal static partial class OtlpMetricTranslatorLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "OTLP metric '{Name}' dropped: kind {Kind} not yet supported in v1")]
    public static partial void MetricKindDropped(this ILogger logger, string name, Metric.DataOneofCase kind);
}
