using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.IntegrationTests;

public sealed class RetentionBatchingTests : IClassFixture<TestHostFixture>
{
    // Past SQLite's 32,766 bound parameters per statement and past one 50,000-row batch.
    private const int ExpiredCount = 60_000;

    private readonly TestHostFixture _fixture;

    public RetentionBatchingTests(TestHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Retention_Deletes_More_Expired_Rows_Than_One_Statement_Can_Bind()
    {
        var resource = NewResource();
        var now = DateTimeOffset.UtcNow;
        var expiredNano = UnixNanoTime.ToUnixNanoseconds(now.AddDays(-40));
        var freshNano = UnixNanoTime.ToUnixNanoseconds(now.AddMinutes(-1));
        long TimeOf(int i) => i < ExpiredCount ? expiredNano + i : freshNano;

        var logs = Enumerable.Range(0, ExpiredCount + 1).Select(i => NewLog(resource, TimeOf(i))).ToList();
        var spans = Enumerable.Range(0, ExpiredCount + 1)
            .Select(i => new Span
            {
                TraceId = TraceId.FromBytes(RandomNumberGenerator.GetBytes(TraceId.SizeInBytes)),
                SpanId = SpanId.FromBytes(RandomNumberGenerator.GetBytes(SpanId.SizeInBytes)),
                ResourceHash = resource.Hash,
                Name = "bulk",
                StartUnixNano = TimeOf(i),
                EndUnixNano = TimeOf(i) + 1,
            })
            .ToList();

        await _fixture.Services.GetRequiredService<ILogSink>()
            .WriteAsync([new LogBatch([resource], logs)], CancellationToken.None);
        await _fixture.Services.GetRequiredService<ITraceSink>()
            .WriteAsync([new TraceBatch([resource], spans)], CancellationToken.None);

        var deletedLogs = await _fixture.Services.GetRequiredService<ILogRetentionPolicy>()
            .EnforceAsync(TimeSpan.FromDays(30), CancellationToken.None);
        var deletedSpans = await _fixture.Services.GetRequiredService<ITraceRetentionPolicy>()
            .EnforceAsync(TimeSpan.FromDays(7), CancellationToken.None);

        deletedLogs.ShouldBe(ExpiredCount);
        deletedSpans.ShouldBe(ExpiredCount);

        await using var context = await CreateContextAsync();
        (await context.Logs.CountAsync(l => l.ResourceHash == resource.Hash)).ShouldBe(1);
        (await context.Spans.CountAsync(s => s.ResourceHash == resource.Hash)).ShouldBe(1);
    }

    [Fact]
    public async Task Retention_Backlog_Leaves_Room_For_Concurrent_Ingestion()
    {
        // A backlog of several batches, as on the first sweep after retention was off or failing.
        const int Backlog = 150_000;
        var resource = NewResource();
        var expiredNano = UnixNanoTime.ToUnixNanoseconds(DateTimeOffset.UtcNow.AddDays(-40));
        var sink = _fixture.Services.GetRequiredService<ILogSink>();
        var sinkMetrics = _fixture.Services.GetRequiredService<TelemetrySinkMetrics>();

        await sink.WriteAsync(
            [new LogBatch([resource], Enumerable.Range(0, Backlog).Select(i => NewLog(resource, expiredNano + i)).ToList())],
            CancellationToken.None);
        var droppedBefore = sinkMetrics.Snapshot().LogsDropped;

        // Own thread, as the retention host and the writer are separate background services
        // (and the SQLite provider runs "async" queries synchronously).
        var policy = _fixture.Services.GetRequiredService<ILogRetentionPolicy>();
        var retention = Task.Run(() => policy.EnforceAsync(TimeSpan.FromDays(30), CancellationToken.None).AsTask());
        var written = 0;
        while (!retention.IsCompleted)
        {
            var fresh = NewLog(resource, UnixNanoTime.ToUnixNanoseconds(DateTimeOffset.UtcNow));
            await sink.WriteAsync([new LogBatch([resource], [fresh])], CancellationToken.None);
            written++;
            await Task.Delay(5);
        }

        (await retention).ShouldBeGreaterThanOrEqualTo(Backlog);
        written.ShouldBeGreaterThan(0);
        sinkMetrics.Snapshot().LogsDropped.ShouldBe(droppedBefore);

        await using var context = await CreateContextAsync();
        (await context.Logs.CountAsync(l => l.ResourceHash == resource.Hash)).ShouldBe(written);
    }

    private static Resource NewResource()
    {
        var service = $"bulk-{Guid.NewGuid():N}";
        return new Resource
        {
            Hash = ResourceHasher.Compute(service, null, null, 0, AttributeMap.Empty),
            ServiceName = service,
        };
    }

    private static LogRecord NewLog(Resource resource, long timeNano) => new()
    {
        ResourceHash = resource.Hash,
        TimeUnixNano = timeNano,
        ObservedTimeUnixNano = timeNano,
        SeverityNumber = SeverityNumber.Info,
        Body = "bulk",
    };

    private async Task<TelemetryDbContext> CreateContextAsync() =>
        await _fixture.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContextAsync();
}
