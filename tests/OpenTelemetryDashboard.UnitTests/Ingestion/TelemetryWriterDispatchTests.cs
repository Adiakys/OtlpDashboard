using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.UnitTests.Ingestion;

public sealed class TelemetryWriterDispatchTests
{
    [Fact]
    public async Task Dispatches_Each_Batch_Type_To_The_Matching_Sink()
    {
        using var cts = new CancellationTokenSource();
        var host = BuildHost();

        await host.Writer.StartAsync(cts.Token);

        await host.Channel.WriteAsync(new TraceBatch([], []), cts.Token);
        await host.Channel.WriteAsync(new LogBatch([], []), cts.Token);
        await host.Channel.WriteAsync(new MetricBatch([], []), cts.Token);

        await host.StopAndDrainAsync(TimeSpan.FromSeconds(5));

        host.TraceSink.ReceivedBatches.ShouldBe(1);
        host.LogSink.ReceivedBatches.ShouldBe(1);
        host.MetricSink.ReceivedBatches.ShouldBe(1);
    }

    [Fact]
    public async Task Drains_Remaining_Metric_Batches_On_Shutdown()
    {
        using var cts = new CancellationTokenSource();
        var host = BuildHost();

        await host.Writer.StartAsync(cts.Token);

        // Enqueue while writer is active; shutdown will drain the tail.
        for (var i = 0; i < 3; i++)
        {
            await host.Channel.WriteAsync(new MetricBatch([], [BuildSample(i)]), cts.Token);
        }

        await host.StopAndDrainAsync(TimeSpan.FromSeconds(5));

        host.MetricSink.TotalSamples.ShouldBe(3);
    }

    private static MetricSample BuildSample(int index)
    {
        var key = InstrumentKey.Create(new byte[32], "tests", $"m.{index}", InstrumentKind.Gauge);
        return new MetricSample(key, new Instrument { Name = $"m.{index}" }, new DataPoint { Value = index });
    }

    private static TestHost BuildHost()
    {
        var channelOptions = Options.Create(new TelemetryChannelOptions
        {
            Capacity = 100,
            MaxBatchSize = 16,
            FlushIntervalMs = 10,
        });
        var shutdownOptions = Options.Create(new IngestionShutdownOptions
        {
            DrainTimeoutSeconds = 5,
        });

        var channel = new TelemetryChannel(channelOptions, NullLogger<TelemetryChannel>.Instance);
        var traceSink = new RecordingTraceSink();
        var logSink = new RecordingLogSink();
        var metricSink = new RecordingMetricSink();

        var writer = new TelemetryWriter(
            channel,
            traceSink,
            logSink,
            metricSink,
            channelOptions,
            shutdownOptions,
            NullLogger<TelemetryWriter>.Instance);

        return new TestHost(channel, traceSink, logSink, metricSink, writer);
    }

    private sealed class TestHost(
        TelemetryChannel channel,
        RecordingTraceSink traceSink,
        RecordingLogSink logSink,
        RecordingMetricSink metricSink,
        TelemetryWriter writer)
    {
        public TelemetryChannel Channel { get; } = channel;
        public RecordingTraceSink TraceSink { get; } = traceSink;
        public RecordingLogSink LogSink { get; } = logSink;
        public RecordingMetricSink MetricSink { get; } = metricSink;
        public TelemetryWriter Writer { get; } = writer;

        public async Task StopAndDrainAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            await Writer.StopAsync(cts.Token);
            Writer.Dispose();
        }
    }

    private sealed class RecordingTraceSink : ITraceSink
    {
        public int ReceivedBatches { get; private set; }
        public Task WriteAsync(IReadOnlyList<TraceBatch> batches, CancellationToken ct)
        {
            ReceivedBatches += batches.Count;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogSink : ILogSink
    {
        public int ReceivedBatches { get; private set; }
        public Task WriteAsync(IReadOnlyList<LogBatch> batches, CancellationToken ct)
        {
            ReceivedBatches += batches.Count;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMetricSink : IMetricSink
    {
        public int ReceivedBatches { get; private set; }
        public int TotalSamples { get; private set; }
        public Task WriteAsync(IReadOnlyList<MetricBatch> batches, CancellationToken ct)
        {
            ReceivedBatches += batches.Count;
            foreach (var batch in batches) TotalSamples += batch.Samples.Count;
            return Task.CompletedTask;
        }
    }
}
