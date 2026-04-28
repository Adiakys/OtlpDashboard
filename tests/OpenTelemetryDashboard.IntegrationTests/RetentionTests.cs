using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Metrics.InMemory;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;

namespace OpenTelemetryDashboard.IntegrationTests;

public sealed class RetentionTests : IClassFixture<TestHostFixture>
{
    private readonly TestHostFixture _fixture;

    public RetentionTests(TestHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task LogRetention_Deletes_Rows_Older_Than_MaxAge()
    {
        using var client = _fixture.CreateClient();
        var now = DateTimeOffset.UtcNow;

        // Two batches distinguished by service.name so we can assert which survived.
        await SeedLogsAsync(client, service: "old-app", time: now.AddDays(-30), count: 3);
        await SeedLogsAsync(client, service: "fresh-app", time: now.AddMinutes(-5), count: 2);

        await WaitForLogCountAsync(expected: 5);

        var policy = _fixture.Services.GetRequiredService<ILogRetentionPolicy>();
        var deleted = await policy.EnforceAsync(TimeSpan.FromDays(7), CancellationToken.None);

        deleted.ShouldBe(3);
        await using var context = await CreateContextAsync();
        var surviving = await context.Logs.ToListAsync();
        surviving.Count.ShouldBe(2);
        foreach (var log in surviving)
        {
            log.TimeUnixNano.ShouldBeGreaterThan(UnixNanoTime.ToUnixNanoseconds(now.AddDays(-7)));
        }
    }

    [Fact]
    public async Task TraceRetention_Deletes_Spans_Older_Than_MaxAge()
    {
        using var client = _fixture.CreateClient();
        var now = DateTimeOffset.UtcNow;

        var oldTraceId = RandomBytes(16);
        var freshTraceId = RandomBytes(16);

        await SeedSpansAsync(client, service: "old-svc", traceIdBytes: oldTraceId,
            start: now.AddDays(-30), spanCount: 2);
        await SeedSpansAsync(client, service: "fresh-svc", traceIdBytes: freshTraceId,
            start: now.AddMinutes(-5), spanCount: 2);

        await WaitForSpanCountAsync(expected: 4);

        var policy = _fixture.Services.GetRequiredService<ITraceRetentionPolicy>();
        var deleted = await policy.EnforceAsync(TimeSpan.FromDays(7), CancellationToken.None);

        deleted.ShouldBe(2);
        await using var context = await CreateContextAsync();
        var surviving = await context.Spans.ToListAsync();
        surviving.Count.ShouldBe(2);
        surviving.ShouldAllBe(s => s.StartUnixNano >= UnixNanoTime.ToUnixNanoseconds(now.AddDays(-7)));
    }

    [Fact]
    public async Task MetricRetention_Drops_Old_Points_And_Empty_Instruments()
    {
        var storage = _fixture.Services.GetRequiredService<InMemoryMetricStorage>();
        var policy = _fixture.Services.GetRequiredService<IMetricRetentionPolicy>();

        var oldKey = InstrumentKey.Create(resourceHash: RandomBytes(32), scopeName: "tests",
            instrumentName: "retention-old", InstrumentKind.Gauge);
        var freshKey = InstrumentKey.Create(resourceHash: RandomBytes(32), scopeName: "tests",
            instrumentName: "retention-fresh", InstrumentKind.Gauge);

        var now = DateTimeOffset.UtcNow;

        storage.TryRecord(oldKey, new Instrument { Name = "retention-old", Kind = InstrumentKind.Gauge },
            NewPoint(now.AddDays(-30), 1.0), serviceName: "old");
        storage.TryRecord(freshKey, new Instrument { Name = "retention-fresh", Kind = InstrumentKind.Gauge },
            NewPoint(now.AddMinutes(-1), 2.0), serviceName: "fresh");

        var dropped = await policy.EnforceAsync(TimeSpan.FromDays(7), CancellationToken.None);

        dropped.ShouldBe(1);
        storage.Keys.ShouldNotContain(oldKey);
        storage.Keys.ShouldContain(freshKey);
        storage.GetPoints(freshKey).Count.ShouldBe(1);

        // Cleanup to keep the singleton tidy for other test classes sharing this fixture.
        storage.TrimOlderThan(now.AddMinutes(1));
    }

    private static DataPoint NewPoint(DateTimeOffset time, double value) => new()
    {
        Value = value,
        TimeUnixNano = UnixNanoTime.ToUnixNanoseconds(time),
    };

    private static async Task SeedLogsAsync(HttpClient client, string service, DateTimeOffset time, int count)
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = service } },
                },
            },
        };
        var scopeLogs = new ScopeLogs { Scope = new InstrumentationScope { Name = "tests" } };
        for (var i = 0; i < count; i++)
        {
            scopeLogs.LogRecords.Add(new OtlpLogRecord
            {
                TimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(time.AddMilliseconds(i)),
                SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber.Info,
                SeverityText = "INFO",
                Body = new AnyValue { StringValue = service },
            });
        }
        resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);

        using var content = new ByteArrayContent(request.ToByteArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        using var response = await client.PostAsync(new Uri("/v1/logs", UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
    }

    private static async Task SeedSpansAsync(
        HttpClient client,
        string service,
        byte[] traceIdBytes,
        DateTimeOffset start,
        int spanCount)
    {
        var request = new ExportTraceServiceRequest();
        var resourceSpans = new ResourceSpans
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = service } },
                },
            },
        };
        var scopeSpans = new ScopeSpans { Scope = new InstrumentationScope { Name = "tests" } };
        var rootSpanId = RandomBytes(8);
        scopeSpans.Spans.Add(new OtlpSpan
        {
            TraceId = ByteString.CopyFrom(traceIdBytes),
            SpanId = ByteString.CopyFrom(rootSpanId),
            Name = $"{service}.root",
            Kind = OtlpSpan.Types.SpanKind.Server,
            StartTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(start),
            EndTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(start.AddMilliseconds(10)),
            Status = new Status { Code = Status.Types.StatusCode.Ok },
        });
        for (var i = 1; i < spanCount; i++)
        {
            scopeSpans.Spans.Add(new OtlpSpan
            {
                TraceId = ByteString.CopyFrom(traceIdBytes),
                SpanId = ByteString.CopyFrom(RandomBytes(8)),
                ParentSpanId = ByteString.CopyFrom(rootSpanId),
                Name = $"{service}.child{i}",
                Kind = OtlpSpan.Types.SpanKind.Internal,
                StartTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(start.AddMilliseconds(i)),
                EndTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(start.AddMilliseconds(i + 5)),
                Status = new Status { Code = Status.Types.StatusCode.Ok },
            });
        }
        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);

        using var content = new ByteArrayContent(request.ToByteArray());
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);
        response.EnsureSuccessStatusCode();
    }

    private async Task WaitForLogCountAsync(int expected, int timeoutSeconds = 10)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var context = await CreateContextAsync();
            if (await context.Logs.CountAsync() >= expected) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Log count did not reach {expected} within {timeoutSeconds}s.");
    }

    private async Task WaitForSpanCountAsync(int expected, int timeoutSeconds = 10)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var context = await CreateContextAsync();
            if (await context.Spans.CountAsync() >= expected) return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Span count did not reach {expected} within {timeoutSeconds}s.");
    }

    private async Task<TelemetryDbContext> CreateContextAsync()
    {
        var factory = _fixture.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private static byte[] RandomBytes(int length)
    {
        var bytes = new byte[length];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
