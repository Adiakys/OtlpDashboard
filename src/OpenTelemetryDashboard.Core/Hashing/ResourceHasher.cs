using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace OpenTelemetryDashboard.Core.Hashing;

/// <summary>
/// Computes a stable SHA-256 digest over a <see cref="Domain.Resource"/>'s
/// logical identity (service metadata + canonically ordered attributes).
/// Output is 32 bytes.
/// </summary>
public static class ResourceHasher
{
    public const int HashSizeInBytes = 32;

    private const byte TagNull = 0x00;
    private const byte TagString = 0x01;
    private const byte TagBool = 0x02;
    private const byte TagInt64 = 0x03;
    private const byte TagDouble = 0x04;
    private const byte TagBytes = 0x05;
    private const byte TagList = 0x06;
    private const byte TagMap = 0x07;

    public static byte[] Compute(
        string? serviceName,
        string? serviceInstanceId,
        string? schemaUrl,
        uint droppedAttributesCount,
        IReadOnlyDictionary<string, object?> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendString(hash, serviceName);
        AppendString(hash, serviceInstanceId);
        AppendString(hash, schemaUrl);
        AppendUInt32(hash, droppedAttributesCount);

        foreach (var entry in EnumerateOrdered(attributes))
        {
            AppendString(hash, entry.Key);
            AppendValue(hash, entry.Value);
        }

        return hash.GetHashAndReset();
    }

    private static IEnumerable<KeyValuePair<string, object?>> EnumerateOrdered(
        IReadOnlyDictionary<string, object?> attributes)
    {
        // Ordinal sort is deterministic across machines and culture settings.
        var keys = attributes.Keys.ToArray();
        Array.Sort(keys, StringComparer.Ordinal);
        foreach (var key in keys)
        {
            yield return new KeyValuePair<string, object?>(key, attributes[key]);
        }
    }

    private static void AppendValue(IncrementalHash hash, object? value)
    {
        switch (value)
        {
            case null:
                hash.AppendData([TagNull]);
                break;
            case string s:
                hash.AppendData([TagString]);
                AppendString(hash, s);
                break;
            case bool b:
                hash.AppendData([TagBool, b ? (byte)1 : (byte)0]);
                break;
            case sbyte i8:
                AppendInt64(hash, i8);
                break;
            case byte u8:
                AppendInt64(hash, u8);
                break;
            case short i16:
                AppendInt64(hash, i16);
                break;
            case ushort u16:
                AppendInt64(hash, u16);
                break;
            case int i32:
                AppendInt64(hash, i32);
                break;
            case uint u32:
                AppendInt64(hash, u32);
                break;
            case long i64:
                AppendInt64(hash, i64);
                break;
            case float f32:
                AppendDouble(hash, f32);
                break;
            case double f64:
                AppendDouble(hash, f64);
                break;
            case byte[] bytes:
                hash.AppendData([TagBytes]);
                AppendUInt32(hash, (uint)bytes.Length);
                hash.AppendData(bytes);
                break;
            case IReadOnlyDictionary<string, object?> map:
                hash.AppendData([TagMap]);
                AppendUInt32(hash, (uint)map.Count);
                foreach (var kv in EnumerateOrdered(map))
                {
                    AppendString(hash, kv.Key);
                    AppendValue(hash, kv.Value);
                }
                break;
            case IEnumerable<object?> list:
                hash.AppendData([TagList]);
                var items = list as IReadOnlyCollection<object?> ?? list.ToArray();
                AppendUInt32(hash, (uint)items.Count);
                foreach (var item in items)
                {
                    AppendValue(hash, item);
                }
                break;
            default:
                // Fallback: treat as string. Unknown types are rare in OTLP attributes.
                hash.AppendData([TagString]);
                AppendString(hash, value.ToString());
                break;
        }
    }

    private static void AppendString(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            hash.AppendData([TagNull]);
            return;
        }

        var byteCount = Encoding.UTF8.GetByteCount(value);
        Span<byte> lengthBuffer = stackalloc byte[5];
        lengthBuffer[0] = TagString;
        BinaryPrimitives.WriteUInt32BigEndian(lengthBuffer[1..], (uint)byteCount);
        hash.AppendData(lengthBuffer);

        if (byteCount <= 1024)
        {
            Span<byte> buffer = stackalloc byte[byteCount];
            Encoding.UTF8.GetBytes(value, buffer);
            hash.AppendData(buffer);
        }
        else
        {
            var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Encoding.UTF8.GetBytes(value, rented.AsSpan(0, byteCount));
                hash.AppendData(rented.AsSpan(0, byteCount));
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[9];
        buffer[0] = TagInt64;
        BinaryPrimitives.WriteInt64BigEndian(buffer[1..], value);
        hash.AppendData(buffer);
    }

    private static void AppendDouble(IncrementalHash hash, double value)
    {
        Span<byte> buffer = stackalloc byte[9];
        buffer[0] = TagDouble;
        BinaryPrimitives.WriteDoubleBigEndian(buffer[1..], value);
        hash.AppendData(buffer);
    }

    private static void AppendUInt32(IncrementalHash hash, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
