using OpenTelemetry.Proto.Common.V1;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Ingestion.Translators;

internal static class OtlpConversion
{
    public static IReadOnlyDictionary<string, object?> ToAttributeMap(IEnumerable<KeyValue> keyValues)
    {
        ArgumentNullException.ThrowIfNull(keyValues);

        Dictionary<string, object?>? dict = null;
        foreach (var kv in keyValues)
        {
            dict ??= new Dictionary<string, object?>(capacity: 8, StringComparer.Ordinal);
            dict[kv.Key] = ToObject(kv.Value);
        }

        return dict is null ? AttributeMap.Empty : dict;
    }

    public static object? ToObject(AnyValue? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.ValueCase switch
        {
            AnyValue.ValueOneofCase.StringValue => value.StringValue,
            AnyValue.ValueOneofCase.BoolValue => value.BoolValue,
            AnyValue.ValueOneofCase.IntValue => value.IntValue,
            AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue,
            AnyValue.ValueOneofCase.BytesValue => value.BytesValue.ToByteArray(),
            AnyValue.ValueOneofCase.ArrayValue => ToArray(value.ArrayValue),
            AnyValue.ValueOneofCase.KvlistValue => ToAttributeMap(value.KvlistValue.Values),
            _ => null,
        };
    }

    private static object?[] ToArray(ArrayValue array)
    {
        if (array.Values.Count == 0)
        {
            return [];
        }

        var result = new object?[array.Values.Count];
        for (var i = 0; i < array.Values.Count; i++)
        {
            result[i] = ToObject(array.Values[i]);
        }
        return result;
    }

    public static string? ExtractStringAttribute(IReadOnlyDictionary<string, object?> attributes, string key) =>
        attributes.TryGetValue(key, out var value) && value is string s ? s : null;
}
