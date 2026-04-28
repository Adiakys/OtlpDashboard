using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace OpenTelemetryDashboard.Core.Domain;

public readonly struct SpanId : IEquatable<SpanId>
{
    public static SpanId Empty => default;

    public const int SizeInBytes = 8;
    private const int HexLength = SizeInBytes * 2;

    private readonly ulong _value;

    private SpanId(ulong value) => _value = value;

    public bool IsEmpty => _value == 0;

    public static SpanId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
        {
            throw new ArgumentException(
                $"SpanId requires exactly {SizeInBytes} bytes, got {bytes.Length}.",
                nameof(bytes));
        }

        return new SpanId(BinaryPrimitives.ReadUInt64BigEndian(bytes));
    }

    public static bool TryParse(ReadOnlySpan<char> hex, out SpanId spanId)
    {
        Span<byte> bytes = stackalloc byte[SizeInBytes];
        if (!HexUtilities.TryParseHex(hex, bytes))
        {
            spanId = default;
            return false;
        }

        spanId = FromBytes(bytes);
        return true;
    }

    public byte[] ToByteArray()
    {
        var buffer = new byte[SizeInBytes];
        WriteBytes(buffer);
        return buffer;
    }

    public void WriteBytes(Span<byte> destination)
    {
        if (destination.Length < SizeInBytes)
        {
            throw new ArgumentException(
                $"Destination must be at least {SizeInBytes} bytes.",
                nameof(destination));
        }

        BinaryPrimitives.WriteUInt64BigEndian(destination, _value);
    }

    public override string ToString()
    {
        return string.Create(HexLength, this, static (span, id) =>
        {
            Span<byte> bytes = stackalloc byte[SizeInBytes];
            id.WriteBytes(bytes);
            HexUtilities.WriteLowerHex(bytes, span);
        });
    }

    public bool Equals(SpanId other) => _value == other._value;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SpanId other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public static bool operator ==(SpanId left, SpanId right) => left.Equals(right);

    public static bool operator !=(SpanId left, SpanId right) => !left.Equals(right);
}
