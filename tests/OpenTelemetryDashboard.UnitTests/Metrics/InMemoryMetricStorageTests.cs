using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence.Metrics.InMemory;

namespace OpenTelemetryDashboard.UnitTests.Metrics;

public sealed class InMemoryMetricStorageTests
{
    private static InMemoryMetricStorage CreateStorage(int maxInstruments = 5, int maxPoints = 4)
    {
        var options = Options.Create(new InMemoryMetricStoreOptions
        {
            MaxInstruments = maxInstruments,
            MaxPointsPerInstrument = maxPoints,
        });
        return new InMemoryMetricStorage(options, NullLogger<InMemoryMetricStorage>.Instance);
    }

    private static Instrument NewInstrument(string name) => new() { Name = name, Kind = InstrumentKind.Gauge };

    private static DataPoint NewPoint(double value) =>
        new() { Value = value, TimeUnixNano = 1 };

    private static InstrumentKey NewKey(string name) =>
        InstrumentKey.Create(new byte[32], scopeName: "tests", name, InstrumentKind.Gauge);

    [Fact]
    public void Records_Are_Visible_As_Points()
    {
        var storage = CreateStorage();
        var key = NewKey("latency");

        storage.TryRecord(key, NewInstrument("latency"), NewPoint(1.0), serviceName: null).ShouldBeTrue();
        storage.TryRecord(key, NewInstrument("latency"), NewPoint(2.0), serviceName: null).ShouldBeTrue();

        storage.GetPoints(key).Select(p => p.Value).ShouldBe([1.0, 2.0]);
        storage.GetInstrument(key).ShouldNotBeNull();
        storage.Keys.ShouldContain(key);
    }

    [Fact]
    public void Drops_New_Instruments_When_Cap_Reached()
    {
        var storage = CreateStorage(maxInstruments: 2);
        storage.TryRecord(NewKey("a"), NewInstrument("a"), NewPoint(1), serviceName: null).ShouldBeTrue();
        storage.TryRecord(NewKey("b"), NewInstrument("b"), NewPoint(1), serviceName: null).ShouldBeTrue();

        storage.TryRecord(NewKey("c"), NewInstrument("c"), NewPoint(1), serviceName: null).ShouldBeFalse();

        storage.Keys.Count.ShouldBe(2);
        storage.GetPoints(NewKey("c")).ShouldBeEmpty();
    }

    [Fact]
    public void Existing_Instrument_Accepts_Points_Even_At_Cap()
    {
        var storage = CreateStorage(maxInstruments: 1, maxPoints: 4);
        var key = NewKey("counter");
        storage.TryRecord(key, NewInstrument("counter"), NewPoint(1), serviceName: null);

        for (var i = 0; i < 10; i++)
        {
            storage.TryRecord(key, NewInstrument("counter"), NewPoint(i), serviceName: null).ShouldBeTrue();
        }

        storage.GetPoints(key).Count.ShouldBe(4);
    }

    [Fact]
    public void TrimOlderThan_Drops_Old_Points_And_Keeps_Fresh_Ones()
    {
        var storage = CreateStorage();
        var key = NewKey("cpu");

        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(-5);
        var t2 = DateTimeOffset.UtcNow;

        storage.TryRecord(key, NewInstrument("cpu"), PointAt(t0, 1.0), serviceName: null);
        storage.TryRecord(key, NewInstrument("cpu"), PointAt(t1, 2.0), serviceName: null);
        storage.TryRecord(key, NewInstrument("cpu"), PointAt(t2, 3.0), serviceName: null);

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-7);
        var dropped = storage.TrimOlderThan(cutoff);

        dropped.ShouldBe(1);
        storage.GetPoints(key).Select(p => p.Value).ShouldBe([2.0, 3.0]);
    }

    [Fact]
    public void TrimOlderThan_Removes_Instruments_That_Become_Empty()
    {
        var storage = CreateStorage();
        var staleKey = NewKey("stale");
        var freshKey = NewKey("fresh");

        var ancient = DateTimeOffset.UtcNow.AddDays(-30);
        var now = DateTimeOffset.UtcNow;

        storage.TryRecord(staleKey, NewInstrument("stale"), PointAt(ancient, 1.0), serviceName: null);
        storage.TryRecord(freshKey, NewInstrument("fresh"), PointAt(now, 2.0), serviceName: null);

        var dropped = storage.TrimOlderThan(DateTimeOffset.UtcNow.AddDays(-1));

        dropped.ShouldBe(1);
        storage.Keys.ShouldNotContain(staleKey);
        storage.Keys.ShouldContain(freshKey);
    }

    [Fact]
    public void TrimOlderThan_With_No_Matches_Is_A_Noop()
    {
        var storage = CreateStorage();
        var key = NewKey("gauge");

        storage.TryRecord(key, NewInstrument("gauge"), PointAt(DateTimeOffset.UtcNow, 1.0), serviceName: null);

        var dropped = storage.TrimOlderThan(DateTimeOffset.UtcNow.AddDays(-1));

        dropped.ShouldBe(0);
        storage.GetPoints(key).Count.ShouldBe(1);
    }

    private static DataPoint PointAt(DateTimeOffset time, double value) => new()
    {
        Value = value,
        TimeUnixNano = UnixNanoTime.ToUnixNanoseconds(time),
    };
}
