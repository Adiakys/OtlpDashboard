using System.Buffers.Text;
using System.Globalization;
using System.Text;
using OpenTelemetryDashboard.Core.Abstractions.Queries;

namespace OpenTelemetryDashboard.Api;

/// <summary>
/// Opaque cursor encoding for keyset pagination. Payload shape is
/// <c>"{tag}:{time}:{key}"</c> ASCII, base64url-encoded.
/// <para>
/// The single-character tag (<c>L</c> for logs, <c>T</c> for traces)
/// prevents a client from accidentally feeding a log-listing cursor into
/// the trace-listing endpoint — <see cref="TryDecodeLog"/> and
/// <see cref="TryDecodeTrace"/> each reject cursors with the wrong tag.
/// </para>
/// <para>
/// Cursors are not security tokens: the encoding only discourages clients
/// from interpreting the payload shape as a stable public contract.
/// </para>
/// </summary>
internal static class CursorCodec
{
    private const char LogTag = 'L';
    private const char TraceTag = 'T';

    public static string EncodeLog(long time, long secondaryKey) =>
        EncodeTagged(LogTag, time, secondaryKey);

    public static string EncodeTrace(long time, long secondaryKey) =>
        EncodeTagged(TraceTag, time, secondaryKey);

    public static bool TryDecodeLog(string? cursor, out CursorPosition value)
    {
        if (TryDecodeTagged(cursor, LogTag, out var time, out var key))
        {
            value = new CursorPosition(time, key);
            return true;
        }

        value = default;
        return false;
    }

    public static bool TryDecodeTrace(string? cursor, out CursorPosition value)
    {
        if (TryDecodeTagged(cursor, TraceTag, out var time, out var key))
        {
            value = new CursorPosition(time, key);
            return true;
        }

        value = default;
        return false;
    }

    private static string EncodeTagged(char tag, long time, long secondaryKey)
    {
        var payload = $"{tag}:{time.ToString(CultureInfo.InvariantCulture)}:{secondaryKey.ToString(CultureInfo.InvariantCulture)}";
        var bytes = Encoding.ASCII.GetBytes(payload);
        return Base64Url.EncodeToString(bytes);
    }

    private static bool TryDecodeTagged(string? cursor, char expectedTag, out long time, out long secondaryKey)
    {
        time = 0;
        secondaryKey = 0;

        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(cursor.AsSpan());
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.ASCII.GetString(bytes);

        // Expected shape: "{tag}:{time}:{secondaryKey}"
        var firstColon = payload.IndexOf(':', StringComparison.Ordinal);
        if (firstColon != 1 || payload.Length < 5 || payload[0] != expectedTag)
        {
            return false;
        }

        var secondColon = payload.IndexOf(':', firstColon + 1);
        if (secondColon <= firstColon + 1 || secondColon >= payload.Length - 1)
        {
            return false;
        }

        return long.TryParse(payload.AsSpan(firstColon + 1, secondColon - firstColon - 1),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out time)
            && long.TryParse(payload.AsSpan(secondColon + 1),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out secondaryKey);
    }
}
