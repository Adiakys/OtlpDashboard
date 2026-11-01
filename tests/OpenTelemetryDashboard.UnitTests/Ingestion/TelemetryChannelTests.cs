using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.UnitTests.Ingestion;

public sealed class TelemetryChannelTests
{
    [Fact]
    public void Rejects_Batches_Beyond_The_Pending_Item_Budget()
    {
        var channel = BuildChannel(maxPendingItems: 10);

        channel.TryWrite(BuildBatch(6)).ShouldBeTrue();
        channel.TryWrite(BuildBatch(4)).ShouldBeTrue();
        channel.TryWrite(BuildBatch(1)).ShouldBeFalse();

        channel.PendingItems.ShouldBe(10);
    }

    [Fact]
    public void Accepts_Again_Once_The_Writer_Releases_Batches()
    {
        var channel = BuildChannel(maxPendingItems: 10);
        channel.TryWrite(BuildBatch(10)).ShouldBeTrue();
        channel.TryWrite(BuildBatch(5)).ShouldBeFalse();

        channel.Reader.TryRead(out var read).ShouldBeTrue();
        channel.Release([read!]);

        channel.PendingItems.ShouldBe(0);
        channel.TryWrite(BuildBatch(5)).ShouldBeTrue();
    }

    [Fact]
    public void Admits_An_Oversized_Batch_While_The_Budget_Is_Not_Exhausted()
    {
        var channel = BuildChannel(maxPendingItems: 10);

        channel.TryWrite(BuildBatch(5)).ShouldBeTrue();
        channel.TryWrite(BuildBatch(50)).ShouldBeTrue();
        channel.TryWrite(BuildBatch(1)).ShouldBeFalse();
    }

    [Fact]
    public void Does_Not_Hold_Budget_For_Batches_Rejected_By_Capacity()
    {
        var channel = BuildChannel(maxPendingItems: 100, capacity: 1);

        channel.TryWrite(BuildBatch(3)).ShouldBeTrue();
        channel.TryWrite(BuildBatch(3)).ShouldBeFalse();

        channel.PendingItems.ShouldBe(3);
    }

    [Fact]
    public void Logs_The_First_Rejection_Then_One_Summary_Per_Interval()
    {
        var logger = new RecordingLogger();
        var time = new ManualTimeProvider();
        var channel = new TelemetryChannel(
            Options.Create(new TelemetryChannelOptions { MaxPendingItems = 1 }),
            logger,
            time);
        channel.TryWrite(BuildBatch(1)).ShouldBeTrue();

        channel.TryWrite(BuildBatch(2)).ShouldBeFalse();
        for (var i = 0; i < 50; i++)
        {
            channel.TryWrite(BuildBatch(3)).ShouldBeFalse();
        }
        logger.Warnings.Count.ShouldBe(1);
        logger.Warnings[0].ShouldContain("Rejected 1 OTLP export requests (2 items)");

        time.Advance(TimeSpan.FromSeconds(10));
        channel.TryWrite(BuildBatch(3)).ShouldBeFalse();

        logger.Warnings.Count.ShouldBe(2);
        logger.Warnings[1].ShouldContain("Rejected 51 OTLP export requests (153 items)");
    }

    private static TelemetryChannel BuildChannel(int maxPendingItems, int capacity = 100) =>
        new(
            Options.Create(new TelemetryChannelOptions { Capacity = capacity, MaxPendingItems = maxPendingItems }),
            NullLogger<TelemetryChannel>.Instance,
            TimeProvider.System);

    private static MetricBatch BuildBatch(int samples) =>
        new([], Enumerable.Range(0, samples).Select(TelemetryWriterDispatchTests.BuildSample).ToList());

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class RecordingLogger : ILogger<TelemetryChannel>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
