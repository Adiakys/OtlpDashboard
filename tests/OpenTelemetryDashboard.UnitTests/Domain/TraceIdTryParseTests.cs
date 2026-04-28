using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.UnitTests.Domain;

public sealed class TraceIdTryParseTests
{
    [Fact]
    public void Round_Trips_ToString_And_TryParse()
    {
        var bytes = new byte[]
        {
            0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef,
            0xfe, 0xdc, 0xba, 0x98, 0x76, 0x54, 0x32, 0x10,
        };
        var traceId = TraceId.FromBytes(bytes);

        TraceId.TryParse(traceId.ToString(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(traceId);
    }

    [Fact]
    public void Accepts_Upper_Case_Hex()
    {
        TraceId.TryParse("0123456789ABCDEFFEDCBA9876543210", out var parsed).ShouldBeTrue();
        parsed.ToString().ShouldBe("0123456789abcdeffedcba9876543210");
    }

    [Fact]
    public void Rejects_Wrong_Length()
    {
        TraceId.TryParse("abc", out _).ShouldBeFalse();
        TraceId.TryParse(new string('0', 33), out _).ShouldBeFalse();
        TraceId.TryParse(new string('0', 31), out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_Non_Hex_Characters()
    {
        TraceId.TryParse("xx23456789abcdeffedcba9876543210", out _).ShouldBeFalse();
    }

    [Fact]
    public void Rejects_Empty()
    {
        TraceId.TryParse(string.Empty, out _).ShouldBeFalse();
    }

    [Fact]
    public void SpanId_Round_Trips()
    {
        var bytes = new byte[] { 0xde, 0xad, 0xbe, 0xef, 0x01, 0x02, 0x03, 0x04 };
        var spanId = SpanId.FromBytes(bytes);

        SpanId.TryParse(spanId.ToString(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(spanId);
    }

    [Fact]
    public void SpanId_Rejects_Wrong_Length()
    {
        SpanId.TryParse("deadbeef", out _).ShouldBeFalse();       // 8 chars, needs 16
        SpanId.TryParse(new string('0', 17), out _).ShouldBeFalse();
    }
}
