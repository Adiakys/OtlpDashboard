using OpenTelemetryDashboard.Api;

namespace OpenTelemetryDashboard.UnitTests.Api;

public sealed class CursorCodecTests
{
    [Fact]
    public void Log_Encode_Then_Decode_Round_Trips()
    {
        var cursor = CursorCodec.EncodeLog(1_700_000_000_000_000_000L, 42L);

        CursorCodec.TryDecodeLog(cursor, out var decoded).ShouldBeTrue();
        decoded.Time.ShouldBe(1_700_000_000_000_000_000L);
        decoded.SecondaryKey.ShouldBe(42L);
    }

    [Fact]
    public void Trace_Encode_Then_Decode_Round_Trips()
    {
        var cursor = CursorCodec.EncodeTrace(1_700_000_000_000_000_000L, 42L);

        CursorCodec.TryDecodeTrace(cursor, out var decoded).ShouldBeTrue();
        decoded.Time.ShouldBe(1_700_000_000_000_000_000L);
        decoded.SecondaryKey.ShouldBe(42L);
    }

    [Fact]
    public void Log_Cursor_Is_Not_Accepted_By_Trace_Decoder()
    {
        var cursor = CursorCodec.EncodeLog(1L, 2L);

        CursorCodec.TryDecodeTrace(cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void Trace_Cursor_Is_Not_Accepted_By_Log_Decoder()
    {
        var cursor = CursorCodec.EncodeTrace(1L, 2L);

        CursorCodec.TryDecodeLog(cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void Decode_Returns_False_For_Null_Or_Empty()
    {
        CursorCodec.TryDecodeLog(null, out _).ShouldBeFalse();
        CursorCodec.TryDecodeLog(string.Empty, out _).ShouldBeFalse();
        CursorCodec.TryDecodeTrace(null, out _).ShouldBeFalse();
    }

    [Fact]
    public void Decode_Returns_False_For_Invalid_Base64()
    {
        CursorCodec.TryDecodeLog("!!!not-base64!!!", out _).ShouldBeFalse();
    }

    [Fact]
    public void Decode_Returns_False_For_Malformed_Payload()
    {
        var missingTag = System.Text.Encoding.ASCII.GetBytes("123:456");
        CursorCodec.TryDecodeLog(
            System.Buffers.Text.Base64Url.EncodeToString(missingTag), out _).ShouldBeFalse();

        var missingColon = System.Text.Encoding.ASCII.GetBytes("L123456");
        CursorCodec.TryDecodeLog(
            System.Buffers.Text.Base64Url.EncodeToString(missingColon), out _).ShouldBeFalse();
    }

    [Fact]
    public void Decode_Returns_False_For_Non_Numeric_Parts()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("L:abc:def");
        var cursor = System.Buffers.Text.Base64Url.EncodeToString(bytes);

        CursorCodec.TryDecodeLog(cursor, out _).ShouldBeFalse();
    }

    [Fact]
    public void Encode_Handles_Zero_Values()
    {
        var cursor = CursorCodec.EncodeLog(0, 0);

        CursorCodec.TryDecodeLog(cursor, out var decoded).ShouldBeTrue();
        decoded.Time.ShouldBe(0L);
        decoded.SecondaryKey.ShouldBe(0L);
    }
}
