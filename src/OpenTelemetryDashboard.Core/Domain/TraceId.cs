using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;

namespace OpenTelemetryDashboard.Core.Domain;

public readonly struct TraceId : IEquatable<TraceId>
{
    public static TraceId Empty => default;

    public const int SizeInBytes = 16;
    private const int HexLength = SizeInBytes * 2;

    private readonly ulong _high;
    private readonly ulong _low;

    private TraceId(ulong high, ulong low)
    {
        _high = high;
        _low = low;
    }

    public bool IsEmpty => _high == 0 && _low == 0;

    public static TraceId FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeInBytes)
        {
            throw new ArgumentException(
                $"TraceId requires exactly {SizeInBytes} bytes, got {bytes.Length}.",
                nameof(bytes));
        }

        return new TraceId(
            BinaryPrimitives.ReadUInt64BigEndian(bytes),
            BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]));
    }

    public static bool TryParse(ReadOnlySpan<char> hex, out TraceId traceId)
    {
        Span<byte> bytes = stackalloc byte[SizeInBytes];
        if (!HexUtilities.TryParseHex(hex, bytes))
        {
            traceId = default;
            return false;
        }

        traceId = FromBytes(bytes);
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

        BinaryPrimitives.WriteUInt64BigEndian(destination, _high);
        BinaryPrimitives.WriteUInt64BigEndian(destination[8..], _low);
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

    public bool Equals(TraceId other) => _high == other._high && _low == other._low;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is TraceId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_high, _low);

    public static bool operator ==(TraceId left, TraceId right) => left.Equals(right);

    public static bool operator !=(TraceId left, TraceId right) => !left.Equals(right);
}
