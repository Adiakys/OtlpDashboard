using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.UnitTests.Domain;

public sealed class SpanIdTests
{
    [Fact]
    public void FromBytes_RoundTrips()
    {
        var source = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        SpanId.FromBytes(source).ToByteArray().ShouldBe(source);
    }

    [Fact]
    public void Empty_IsIdentifiedAsEmpty()
    {
        SpanId.Empty.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void ToString_Produces_Lowercase_Hex()
    {
        var bytes = new byte[] { 0xAB, 0xCD, 0xEF, 0x01, 0x23, 0x45, 0x67, 0x89 };
        SpanId.FromBytes(bytes).ToString().ShouldBe("abcdef0123456789");
    }
}
