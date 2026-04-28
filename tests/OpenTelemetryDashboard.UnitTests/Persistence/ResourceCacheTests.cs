using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.UnitTests.Persistence;

public sealed class ResourceCacheTests
{
    [Fact]
    public void Remembers_Added_Hashes()
    {
        var cache = new ResourceCache(maxSize: 8);
        var hash = new byte[32];
        Random.Shared.NextBytes(hash);

        cache.Contains(hash).ShouldBeFalse();
        cache.Add(hash);
        cache.Contains((byte[])hash.Clone()).ShouldBeTrue(); // compares by content
    }

    [Fact]
    public void Evicts_Oldest_When_Over_Capacity()
    {
        var cache = new ResourceCache(maxSize: 3);
        var h1 = RandomHash();
        var h2 = RandomHash();
        var h3 = RandomHash();
        var h4 = RandomHash();

        cache.Add(h1);
        cache.Add(h2);
        cache.Add(h3);
        cache.Count.ShouldBe(3);

        cache.Add(h4);

        cache.Count.ShouldBe(3);
        cache.Contains(h1).ShouldBeFalse();  // oldest evicted
        cache.Contains(h4).ShouldBeTrue();
    }

    [Fact]
    public void Contains_Refreshes_Recency()
    {
        var cache = new ResourceCache(maxSize: 2);
        var h1 = RandomHash();
        var h2 = RandomHash();
        var h3 = RandomHash();

        cache.Add(h1);
        cache.Add(h2);
        cache.Contains(h1).ShouldBeTrue(); // bumps h1 to front
        cache.Add(h3);                      // evicts LRU = h2

        cache.Contains(h1).ShouldBeTrue();
        cache.Contains(h2).ShouldBeFalse();
        cache.Contains(h3).ShouldBeTrue();
    }

    private static byte[] RandomHash()
    {
        var b = new byte[32];
        Random.Shared.NextBytes(b);
        return b;
    }
}
