using OpenTelemetryDashboard.Persistence.Metrics.InMemory;

namespace OpenTelemetryDashboard.UnitTests.Metrics;

public sealed class RingBufferTests
{
    [Fact]
    public void Empty_Buffer_Returns_Empty_Snapshot()
    {
        var buffer = new RingBuffer<int>(4);

        buffer.Count.ShouldBe(0);
        buffer.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public void Preserves_Order_Below_Capacity()
    {
        var buffer = new RingBuffer<int>(4);
        buffer.Write(1);
        buffer.Write(2);
        buffer.Write(3);

        buffer.Snapshot().ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Overwrites_Oldest_When_Full()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Write(1);
        buffer.Write(2);
        buffer.Write(3);
        buffer.Write(4); // overwrites 1
        buffer.Write(5); // overwrites 2

        buffer.Count.ShouldBe(3);
        buffer.Snapshot().ShouldBe([3, 4, 5]);
    }

    [Fact]
    public void Zero_Capacity_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }

    [Fact]
    public void RemoveWhile_Drops_Leading_Matches_And_Preserves_The_Rest()
    {
        var buffer = new RingBuffer<int>(5);
        buffer.Write(1);
        buffer.Write(2);
        buffer.Write(3);
        buffer.Write(4);

        var dropped = buffer.RemoveWhile(v => v < 3);

        dropped.ShouldBe(2);
        buffer.Snapshot().ShouldBe([3, 4]);
    }

    [Fact]
    public void RemoveWhile_Stops_At_First_Non_Match()
    {
        var buffer = new RingBuffer<int>(5);
        buffer.Write(1);
        buffer.Write(5);
        buffer.Write(2);
        buffer.Write(6);

        // Only the leading "1" matches; "5" stops the scan even though "2" matches.
        var dropped = buffer.RemoveWhile(v => v < 3);

        dropped.ShouldBe(1);
        buffer.Snapshot().ShouldBe([5, 2, 6]);
    }

    [Fact]
    public void RemoveWhile_All_Matching_Empties_Buffer()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Write(1);
        buffer.Write(2);
        buffer.Write(3);

        var dropped = buffer.RemoveWhile(_ => true);

        dropped.ShouldBe(3);
        buffer.Count.ShouldBe(0);
        buffer.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public void RemoveWhile_After_Overwrite_Drops_In_Insertion_Order()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Write(1);
        buffer.Write(2);
        buffer.Write(3);
        buffer.Write(4); // drops 1, buffer holds 2,3,4
        buffer.Write(5); // drops 2, buffer holds 3,4,5

        var dropped = buffer.RemoveWhile(v => v < 5);

        dropped.ShouldBe(2);
        buffer.Snapshot().ShouldBe([5]);

        // After trimming, subsequent writes continue to work correctly.
        buffer.Write(6);
        buffer.Write(7);
        buffer.Snapshot().ShouldBe([5, 6, 7]);
    }

    [Fact]
    public void RemoveWhile_No_Match_Returns_Zero()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Write(10);
        buffer.Write(20);

        var dropped = buffer.RemoveWhile(v => v < 5);

        dropped.ShouldBe(0);
        buffer.Snapshot().ShouldBe([10, 20]);
    }
}
