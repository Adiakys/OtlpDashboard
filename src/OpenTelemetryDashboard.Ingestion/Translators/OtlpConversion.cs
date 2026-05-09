using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Ingestion.Translators;

internal static class OtlpConversion
{
    public static IReadOnlyDictionary<string, object?> ToAttributeMap(IEnumerable<KeyValue> keyValues)
    {
        ArgumentNullException.ThrowIfNull(keyValues);

        Dictionary<string, object?>? dict = null;
        var added = 0;
        foreach (var kv in keyValues)
        {
            if (added >= OtlpTranslationLimits.MaxAttributesPerEntity) break;
            dict ??= new Dictionary<string, object?>(capacity: 8, StringComparer.Ordinal);
            dict[kv.Key] = ToObject(kv.Value, depth: 0);
            added++;
        }

        return dict is null ? AttributeMap.Empty : dict;
    }

    public static object? ToObject(AnyValue? value) => ToObject(value, depth: 0);

    private static object? ToObject(AnyValue? value, int depth)
    {
        if (value is null)
        {
            return null;
        }
        // Refuse rather than truncate: a hostile producer can sink a
        // sentinel deep enough to blow the stack, and the legitimate
        // shape we care about (span/log attributes) never goes anywhere
        // near 8 levels of nesting.
        if (depth >= OtlpTranslationLimits.MaxAttributeDepth)
        {
            return null;
        }

        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => Truncate(value.StringValue),
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.BytesValue => value.BytesValue.ToByteArray(),
            AnyValue.ValueOneofCase.ArrayValue => ToArray(value.ArrayValue, depth + 1),
            AnyValue.ValueOneofCase.KvlistValue => ToKvList(value.KvlistValue.Values, depth + 1),
            _ => null,
        };
    }

    private static object?[] ToArray(ArrayValue array, int depth)
    {
        if (array.Values.Count == 0)
        {
            return [];
        }

        var size = Math.Min(array.Values.Count, OtlpTranslationLimits.MaxAttributeCollectionSize);
        var result = new object?[size];
        for (var i = 0; i < size; i++)
        {
            result[i] = ToObject(array.Values[i], depth);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, object?> ToKvList(RepeatedField<KeyValue> values, int depth)
    {
        if (values.Count == 0)
        {
            return AttributeMap.Empty;
        }

        var size = Math.Min(values.Count, OtlpTranslationLimits.MaxAttributeCollectionSize);
        var dict = new Dictionary<string, object?>(capacity: size, StringComparer.Ordinal);
        for (var i = 0; i < size; i++)
        {
            dict[values[i].Key] = ToObject(values[i].Value, depth);
        }
        return dict;
    }

    private static string Truncate(string value)
    {
        if (value.Length <= OtlpTranslationLimits.MaxAttributeStringLength)
        {
            return value;
        }
        return string.Concat(
            value.AsSpan(0, OtlpTranslationLimits.MaxAttributeStringLength),
            OtlpTranslationLimits.TruncationSuffix);
    }

    public static string? ExtractStringAttribute(IReadOnlyDictionary<string, object?> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && value is string s ? s : null;
}
