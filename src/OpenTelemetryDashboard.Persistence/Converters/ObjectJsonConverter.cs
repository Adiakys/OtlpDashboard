using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTelemetryDashboard.Persistence.Converters;

/// <summary>
/// System.Text.Json by default deserializes <c>object?</c> into <see cref="JsonElement"/>,
/// which surfaces in the domain as structured tokens instead of native .NET primitives.
/// This converter restores the natural mapping: string/long/double/bool/null/list/dict.
/// </summary>
public sealed class ObjectJsonConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                if (reader.TryGetInt64(out var intValue))
                {
                    return intValue;
                }
                return reader.GetDouble();
            case JsonTokenType.StartArray:
                return ReadArray(ref reader, options);
            case JsonTokenType.StartObject:
                return ReadObject(ref reader, options);
            default:
                return JsonDocument.ParseValue(ref reader).RootElement;
        }
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }

    private static List<object?> ReadArray(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var list = new List<object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            list.Add(ReadValue(ref reader, options));
        }
        return list;
    }

    private static Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString() ?? string.Empty;
            reader.Read();
            dict[key] = ReadValue(ref reader, options);
        }
        return dict;
    }

    private static object? ReadValue(ref Utf8JsonReader reader, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l : reader.GetDouble(),
            JsonTokenType.StartArray => ReadArray(ref reader, options),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => JsonDocument.ParseValue(ref reader).RootElement,
        };
}
