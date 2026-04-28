using Google.Protobuf.Collections;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Common.V1;
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
///
/// Modeling choice: the domain <see cref="DataPoint"/> stores a single scalar
/// <see cref="DataPoint.Value"/>, so aggregate kinds are flattened into one
/// scalar per source data point and the lost detail is preserved as synthetic
/// attributes (prefixed with <c>_</c>) so it remains queryable from the UI.
///
///  - <b>Gauge</b> / <b>Sum</b>: one point per <see cref="NumberDataPoint"/>,
///    value taken as-is.
///  - <b>Histogram</b> / <b>ExponentialHistogram</b>: one point per data point,
///    value = sum / count (mean); count, sum and optional min/max are added as
///    <c>_count</c>, <c>_sum</c>, <c>_min</c>, <c>_max</c>.
///  - <b>Summary</b>: one point per <see cref="SummaryDataPoint.Types.ValueAtQuantile"/>,
///    value = the quantile's value; <c>_count</c>, <c>_sum</c>, <c>quantile</c>
///    (string label like "0.5") are added so split-by quantile draws one line
///    per percentile.
/// </summary>
public sealed class OtlpMetricTranslator
{
    private const string CountAttribute = "_count";
    private const string SumAttribute = "_sum";
    private const string MinAttribute = "_min";
    private const string MaxAttribute = "_max";
    private const string QuantileAttribute = "quantile";

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
                AppendHistogramSamples(
                    samples,
                    resourceHash,
                    scopeName,
                    metric,
                    metric.Histogram.DataPoints,
                    MapTemporality(metric.Histogram.AggregationTemporality));
                break;

            case Metric.DataOneofCase.ExponentialHistogram:
                AppendExponentialHistogramSamples(
                    samples,
                    resourceHash,
                    scopeName,
                    metric,
                    metric.ExponentialHistogram.DataPoints,
                    MapTemporality(metric.ExponentialHistogram.AggregationTemporality));
                break;

            case Metric.DataOneofCase.Summary:
                AppendSummarySamples(
                    samples,
                    resourceHash,
                    scopeName,
                    metric,
                    metric.Summary.DataPoints);
                break;

            default:
                _logger.MetricKindDropped(metric.Name, metric.DataCase);
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

        var instrument = BuildInstrument(metric, kind, temporality, isMonotonic);
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

            samples.Add(new MetricSample(key, instrument, new DataPoint
            {
                StartTimeUnixNano = (long)point.StartTimeUnixNano,
                TimeUnixNano = (long)point.TimeUnixNano,
                Value = value,
                Attributes = OtlpConversion.ToAttributeMap(point.Attributes),
            }));
        }
    }

    private static void AppendHistogramSamples(
        List<MetricSample> samples,
        byte[] resourceHash,
        string scopeName,
        Metric metric,
        RepeatedField<HistogramDataPoint> points,
        Core.Domain.AggregationTemporality temporality)
    {
        if (points.Count == 0) return;

        var instrument = BuildInstrument(metric, InstrumentKind.Histogram, temporality, isMonotonic: false);
        var key = InstrumentKey.Create(resourceHash, scopeName, metric.Name, InstrumentKind.Histogram);

        foreach (var point in points)
        {
            if (point.Count == 0) continue;
            var mean = point.Sum / point.Count;
            if (double.IsNaN(mean) || double.IsInfinity(mean)) continue;

            var attrs = MergeAttributes(point.Attributes, extras =>
            {
                extras[CountAttribute] = (long)point.Count;
                extras[SumAttribute] = point.Sum;
                if (point.HasMin) extras[MinAttribute] = point.Min;
                if (point.HasMax) extras[MaxAttribute] = point.Max;
            });

            samples.Add(new MetricSample(key, instrument, new DataPoint
            {
                StartTimeUnixNano = (long)point.StartTimeUnixNano,
                TimeUnixNano = (long)point.TimeUnixNano,
                Value = mean,
                Attributes = attrs,
            }));
        }
    }

    private static void AppendExponentialHistogramSamples(
        List<MetricSample> samples,
        byte[] resourceHash,
        string scopeName,
        Metric metric,
        RepeatedField<ExponentialHistogramDataPoint> points,
        Core.Domain.AggregationTemporality temporality)
    {
        if (points.Count == 0) return;

        var instrument = BuildInstrument(metric, InstrumentKind.ExponentialHistogram, temporality, isMonotonic: false);
        var key = InstrumentKey.Create(resourceHash, scopeName, metric.Name, InstrumentKind.ExponentialHistogram);

        foreach (var point in points)
        {
            if (point.Count == 0) continue;
            var mean = point.Sum / point.Count;
            if (double.IsNaN(mean) || double.IsInfinity(mean)) continue;

            var attrs = MergeAttributes(point.Attributes, extras =>
            {
                extras[CountAttribute] = (long)point.Count;
                extras[SumAttribute] = point.Sum;
                if (point.HasMin) extras[MinAttribute] = point.Min;
                if (point.HasMax) extras[MaxAttribute] = point.Max;
            });

            samples.Add(new MetricSample(key, instrument, new DataPoint
            {
                StartTimeUnixNano = (long)point.StartTimeUnixNano,
                TimeUnixNano = (long)point.TimeUnixNano,
                Value = mean,
                Attributes = attrs,
            }));
        }
    }

    private static void AppendSummarySamples(
        List<MetricSample> samples,
        byte[] resourceHash,
        string scopeName,
        Metric metric,
        RepeatedField<SummaryDataPoint> points)
    {
        if (points.Count == 0) return;

        var instrument = BuildInstrument(metric, InstrumentKind.Summary, Core.Domain.AggregationTemporality.Cumulative, isMonotonic: false);
        var key = InstrumentKey.Create(resourceHash, scopeName, metric.Name, InstrumentKind.Summary);

        foreach (var point in points)
        {
            if (point.QuantileValues.Count == 0) continue;

            foreach (var q in point.QuantileValues)
            {
                if (double.IsNaN(q.Value) || double.IsInfinity(q.Value)) continue;

                var attrs = MergeAttributes(point.Attributes, extras =>
                {
                    extras[CountAttribute] = (long)point.Count;
                    extras[SumAttribute] = point.Sum;
                    extras[QuantileAttribute] = FormatQuantile(q.Quantile);
                });

                samples.Add(new MetricSample(key, instrument, new DataPoint
                {
                    StartTimeUnixNano = (long)point.StartTimeUnixNano,
                    TimeUnixNano = (long)point.TimeUnixNano,
                    Value = q.Value,
                    Attributes = attrs,
                }));
            }
        }
    }

    private static Instrument BuildInstrument(
        Metric metric,
        InstrumentKind kind,
        Core.Domain.AggregationTemporality temporality,
        bool isMonotonic) => new()
        {
            Name = metric.Name,
            Description = string.IsNullOrEmpty(metric.Description) ? null : metric.Description,
            Unit = string.IsNullOrEmpty(metric.Unit) ? null : metric.Unit,
            Kind = kind,
            IsMonotonic = isMonotonic,
            Temporality = temporality,
        };

    private static Dictionary<string, object?> MergeAttributes(
        IEnumerable<KeyValue> originals,
        Action<Dictionary<string, object?>> addExtras)
    {
        var dict = new Dictionary<string, object?>(capacity: 8, StringComparer.Ordinal);
        foreach (var kv in originals)
        {
            dict[kv.Key] = OtlpConversion.ToObject(kv.Value);
        }
        addExtras(dict);
        return dict;
    }

    private static string FormatQuantile(double q)
    {
        // Emit a stable, locale-independent label. "0.5", "0.95", "0.99" cover
        // the common cases; round to 4 decimals for unusual SDK choices.
        if (Math.Abs(q - Math.Round(q, 2)) < 1e-9)
        {
            return q.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
        return q.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
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
