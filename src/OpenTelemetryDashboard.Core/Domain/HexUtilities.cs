namespace OpenTelemetryDashboard.Core.Domain;

internal static class HexUtilities
{
    public static void WriteLowerHex(ReadOnlySpan<byte> source, Span<char> destination)
    {
        if (destination.Length < source.Length * 2)
        {
            throw new ArgumentException("Destination is too small for hex encoding.", nameof(destination));
        }

        for (var i = 0; i < source.Length; i++)
        {
            destination[i * 2]     = ToHexChar(source[i] >> 4);
            destination[i * 2 + 1] = ToHexChar(source[i] & 0x0F);
        }
    }

    public static bool TryParseHex(ReadOnlySpan<char> source, Span<byte> destination)
    {
        if (source.Length != destination.Length * 2)
        {
            return false;
        }

        for (var i = 0; i < destination.Length; i++)
        {
            if (!TryFromHexChar(source[i * 2], out var hi) ||
                !TryFromHexChar(source[i * 2 + 1], out var lo))
            {
                return false;
            }

            destination[i] = (byte)((hi << 4) | lo);
        }

        return true;
    }

    private static char ToHexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    private static bool TryFromHexChar(char c, out int value)
    {
        if (c is >= '0' and <= '9') { value = c - '0'; return true; }
        if (c is >= 'a' and <= 'f') { value = c - 'a' + 10; return true; }
        if (c is >= 'A' and <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }
}
