using System.Diagnostics.CodeAnalysis;

namespace OpenTelemetryDashboard.Core.Common;

public sealed class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{
    public static ByteArrayEqualityComparer Instance { get; } = new();

    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.AsSpan().SequenceEqual(y);
    }

    public int GetHashCode([DisallowNull] byte[] obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var hash = new HashCode();
        hash.AddBytes(obj);
        return hash.ToHashCode();
    }
}
