using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.UnitTests.Domain;

public sealed class TraceIdTests
{
    [Fact]
    public void FromBytes_RoundTrips_ToByteArray()
    {
        var source = new byte[16];
        Random.Shared.NextBytes(source);

        var id = TraceId.FromBytes(source);

        id.ToByteArray().ShouldBe(source);
    }

    [Fact]
    public void FromBytes_Rejects_WrongLength()
    {
        Should.Throw<ArgumentException>(() => TraceId.FromBytes(new byte[15]));
        Should.Throw<ArgumentException>(() => TraceId.FromBytes(new byte[17]));
    }

    [Fact]
    public void Empty_IsIdentifiedAsEmpty()
    {
        TraceId.Empty.IsEmpty.ShouldBeTrue();
        TraceId.FromBytes(new byte[16]).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void NonZero_IsNotEmpty()
    {
        var bytes = new byte[16];
        bytes[15] = 1;
        TraceId.FromBytes(bytes).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void ToString_Produces_Lowercase_Hex()
    {
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB };
        var id = TraceId.FromBytes(bytes);
        id.ToString().ShouldBe("deadbeef00112233445566778899aabb");
    }

    [Fact]
    public void Equality_Is_ByValue()
    {
        var a1 = TraceId.FromBytes(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
        var a2 = TraceId.FromBytes(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
        var b = TraceId.FromBytes(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());

        (a1 == a2).ShouldBeTrue();
        (a1 != b).ShouldBeTrue();
        a1.GetHashCode().ShouldBe(a2.GetHashCode());
    }
}
