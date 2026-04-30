using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Metrics.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace OpenTelemetryDashboard.IntegrationTests;

public sealed class OtlpHttpIngestionTests : IClassFixture<TestHostFixture>
{
    private readonly TestHostFixture _fixture;

    public OtlpHttpIngestionTests(TestHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Health_Endpoint_Returns_Ok()
    {
        using var client = _fixture.CreateClient();
        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Wrong_Content_Type_Returns_415()
    {
        using var client = _fixture.CreateClient();
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Malformed_Protobuf_Returns_400()
    {
        using var client = _fixture.CreateClient();
        using var content = new ByteArrayContent([0xFF, 0xFF, 0xFF]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Traces_Persists_Resource_And_Span()
    {
        using var client = _fixture.CreateClient();

        var traceIdBytes = RandomBytes(16);
        var spanIdBytes = RandomBytes(8);
        var spanName = $"http.GET.{Guid.NewGuid():N}";

        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new OtlpResource
                    {
                        Attributes =
                        {
                            new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "http-svc" } },
                        },
                    },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "tests", Version = "1.0" },
                            Spans =
                            {
                                new OtlpSpan
                                {
                                    TraceId = ByteString.CopyFrom(traceIdBytes),
                                    SpanId = ByteString.CopyFrom(spanIdBytes),
                                    Name = spanName,
                                    Kind = OtlpSpan.Types.SpanKind.Server,
                                    StartTimeUnixNano = 1,
                                    EndTimeUnixNano = 2,
                                    Status = new Status { Code = Status.Types.StatusCode.Ok },
                                },
                            },
                        },
                    },
                },
            },
        };

        using var response = await PostProtobufAsync(client, "/v1/traces", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitForAsync(async ctx => await ctx.Spans.AnyAsync(s => s.Name == spanName));

        await using var context = await CreateContextAsync();
        var persisted = await context.Spans.AsNoTracking().FirstAsync(s => s.Name == spanName);
        persisted.TraceId.ToByteArray().ShouldBe(traceIdBytes);
        persisted.SpanId.ToByteArray().ShouldBe(spanIdBytes);
        persisted.ResourceHash.Length.ShouldBe(32);

        var resource = await context.Resources.AsNoTracking().FirstAsync(r => r.ServiceName == "http-svc");
        resource.ServiceName.ShouldBe("http-svc");
    }

    [Fact]
    public async Task Span_With_Zero_TraceId_Is_Rejected_Silently()
    {
        using var client = _fixture.CreateClient();

        var request = new ExportTraceServiceRequest
        {
            ResourceSpans =
            {
                new ResourceSpans
                {
                    Resource = new OtlpResource(),
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "tests" },
                            Spans =
                            {
                                new OtlpSpan
                                {
                                    TraceId = ByteString.CopyFrom(new byte[16]), // all zero
                                    SpanId = ByteString.CopyFrom(RandomBytes(8)),
                                    Name = "should.be.dropped",
                                    StartTimeUnixNano = 1,
                                    EndTimeUnixNano = 2,
                                },
                            },
                        },
                    },
                },
            },
        };

        using var response = await PostProtobufAsync(client, "/v1/traces", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Task.Delay(TimeSpan.FromMilliseconds(500));

        await using var context = await CreateContextAsync();
        var count = await context.Spans.CountAsync(s => s.Name == "should.be.dropped");
        count.ShouldBe(0);
    }

    [Fact]
    public async Task Post_Logs_Persists_LogRecord()
    {
        using var client = _fixture.CreateClient();

        var request = new ExportLogsServiceRequest
        {
            ResourceLogs =
            {
                new ResourceLogs
                {
                    Resource = new OtlpResource
                    {
                        Attributes =
                        {
                            new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "logs-svc" } },
                        },
                    },
                    ScopeLogs =
                    {
                        new ScopeLogs
                        {
                            Scope = new InstrumentationScope { Name = "tests" },
                            LogRecords =
                            {
                                new OtlpLogRecord
                                {
                                    TimeUnixNano = 1,
                                    SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber.Info,
                                    SeverityText = "INFO",
                                    Body = new AnyValue { StringValue = "user.login" },
                                },
                            },
                        },
                    },
                },
            },
        };

        using var response = await PostProtobufAsync(client, "/v1/logs", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await WaitForAsync(async ctx => await ctx.Logs.AnyAsync(l => l.Body == "user.login"));

        await using var context = await CreateContextAsync();
        var log = await context.Logs.AsNoTracking().FirstAsync(l => l.Body == "user.login");
        log.SeverityNumber.ShouldBe(Core.Domain.SeverityNumber.Info);
        log.SeverityText.ShouldBe("INFO");
    }

    [Fact]
    public async Task Post_Metrics_Records_Gauge_In_Store()
    {
        using var client = _fixture.CreateClient();

        var request = new ExportMetricsServiceRequest
        {
            ResourceMetrics =
            {
                new ResourceMetrics
                {
                    Resource = new OtlpResource
                    {
                        Attributes =
                        {
                            new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "metrics-svc" } },
                        },
                    },
                    ScopeMetrics =
                    {
                        new ScopeMetrics
                        {
                            Scope = new InstrumentationScope { Name = "tests" },
                            Metrics =
                            {
                                new Metric
                                {
                                    Name = "temperature",
                                    Unit = "celsius",
                                    Gauge = new Gauge
                                    {
                                        DataPoints =
                                        {
                                            new NumberDataPoint
                                            {
                                                TimeUnixNano = 10,
                                                AsDouble = 42.5,
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        using var response = await PostProtobufAsync(client, "/v1/metrics", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var reader = _fixture.Services.GetRequiredService<IMetricReader>();

        InstrumentSummary? matched = null;
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var summaries = await reader.ListInstrumentsAsync(CancellationToken.None);
            matched = summaries.FirstOrDefault(s => s.Key.InstrumentName == "temperature");
            if (matched is { PointCount: >= 1 })
            {
                break;
            }
            matched = null;
            await Task.Delay(50);
        }

        matched.ShouldNotBeNull();
        var series = await reader.GetSeriesAsync(matched!.Key, window: null, includeAttributes: false, CancellationToken.None);
        series.ShouldNotBeNull();
        series!.Points.Count.ShouldBe(1);
        series.Points[0].Value.ShouldBe(42.5);
    }

    private static async Task<HttpResponseMessage> PostProtobufAsync<T>(HttpClient client, string path, T message)
        where T : IMessage<T>
    {
        using var content = new ByteArrayContent(message.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return await client.PostAsync(new Uri(path, UriKind.Relative), content);
    }

    private async Task<TelemetryDbContext> CreateContextAsync()
    {
        var factory = _fixture.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private async Task WaitForAsync(
        Func<TelemetryDbContext, Task<bool>> predicate,
        int timeoutSeconds = 10)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var context = await CreateContextAsync();
            if (await predicate(context))
            {
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException("Database state did not reach the expected condition in time.");
    }

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        Random.Shared.NextBytes(b);
        return b;
    }
}
