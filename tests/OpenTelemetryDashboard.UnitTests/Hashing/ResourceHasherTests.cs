using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;

namespace OpenTelemetryDashboard.UnitTests.Hashing;

public sealed class ResourceHasherTests
{
    [Fact]
    public void Is_Deterministic()
    {
        var attrs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["host.name"] = "pod-1",
            ["process.pid"] = 1234L,
        };

        var h1 = ResourceHasher.Compute("svc", "instance-a", "schema://v1", 0, attrs);
        var h2 = ResourceHasher.Compute("svc", "instance-a", "schema://v1", 0, attrs);

        h1.Length.ShouldBe(ResourceHasher.HashSizeInBytes);
        h1.ShouldBe(h2);
    }

    [Fact]
    public void Is_Insensitive_To_Attribute_Order()
    {
        var a = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["a"] = 1L,
            ["b"] = "x",
            ["c"] = true,
        };
        var b = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["c"] = true,
            ["b"] = "x",
            ["a"] = 1L,
        };

        ResourceHasher.Compute("s", null, null, 0, a)
            .ShouldBe(ResourceHasher.Compute("s", null, null, 0, b));
    }

    [Fact]
    public void Differs_When_Service_Name_Changes()
    {
        var attrs = AttributeMap.Empty;
        var hA = ResourceHasher.Compute("a", null, null, 0, attrs);
        var hB = ResourceHasher.Compute("b", null, null, 0, attrs);
        hA.ShouldNotBe(hB);
    }

    [Fact]
    public void Distinguishes_Between_Typed_Values()
    {
        var intAttrs = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = 42L };
        var strAttrs = new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = "42" };

        ResourceHasher.Compute("s", null, null, 0, intAttrs)
            .ShouldNotBe(ResourceHasher.Compute("s", null, null, 0, strAttrs));
    }
}
