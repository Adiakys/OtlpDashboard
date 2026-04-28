using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence.Metrics.InMemory;

namespace OpenTelemetryDashboard.UnitTests.Metrics;

public sealed class InMemoryMetricSinkAndReaderTests
{
    private static (InMemoryMetricSink Sink, InMemoryMetricReader Reader) BuildPair()
    {
        var options = Options.Create(new InMemoryMetricStoreOptions
        {
            MaxInstruments = 100,
            MaxPointsPerInstrument = 10,
        });
        var storage = new InMemoryMetricStorage(options, NullLogger<InMemoryMetricStorage>.Instance);
        return (new InMemoryMetricSink(storage), new InMemoryMetricReader(storage));
    }

    [Fact]
    public async Task Sink_Writes_Visible_To_Reader()
    {
        var (sink, reader) = BuildPair();
        var hash = new byte[32];
        var key = InstrumentKey.Create(hash, "scope", "rps", InstrumentKind.Sum);
        var instrument = new Instrument { Name = "rps", Kind = InstrumentKind.Sum };

        var batch = new MetricBatch(
            Resources: [],
            Samples:
            [
                new MetricSample(key, instrument, new DataPoint { Value = 1.5, TimeUnixNano = 1 }),
                new MetricSample(key, instrument, new DataPoint { Value = 2.5, TimeUnixNano = 2 }),
            ]);

        await sink.WriteAsync([batch], CancellationToken.None);

        reader.GetInstrumentKeys().ShouldContain(key);
        reader.GetInstrument(key).ShouldNotBeNull();
        reader.GetPoints(key).Select(p => p.Value).ShouldBe([1.5, 2.5]);
    }

    [Fact]
    public async Task Empty_Batch_List_Is_A_Noop()
    {
        var (sink, reader) = BuildPair();

        await sink.WriteAsync([], CancellationToken.None);

        reader.GetInstrumentKeys().ShouldBeEmpty();
    }
}
