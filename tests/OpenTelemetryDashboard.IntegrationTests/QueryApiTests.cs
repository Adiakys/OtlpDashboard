using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;

namespace OpenTelemetryDashboard.IntegrationTests;

public sealed class QueryApiTests : IClassFixture<TestHostFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TestHostFixture _fixture;

    public QueryApiTests(TestHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetLogs_Missing_From_And_To_Returns_400()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/logs", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLogs_Window_Too_Large_Returns_400()
    {
        using var client = _fixture.CreateClient();
        var from = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(30);

        using var response = await client.GetAsync(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLogs_From_After_To_Returns_400()
    {
        using var client = _fixture.CreateClient();
        var from = new DateTimeOffset(2030, 1, 2, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(-1);

        using var response = await client.GetAsync(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLogs_Empty_Window_Returns_Empty_Items()
    {
        using var client = _fixture.CreateClient();
        var from = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        var response = await client.GetFromJsonAsync<PagedLogsResponse>(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}", UriKind.Relative),
            JsonOptions);

        response.ShouldNotBeNull();
        response!.Items.ShouldBeEmpty();
        response.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task GetLogs_Returns_Records_Within_Window()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 2, 10, 12, 0, 0, TimeSpan.Zero);
        var marker = $"query-basic-{Guid.NewGuid():N}";
        await SeedLogsAsync(client, anchor, marker, count: 3);
        await WaitForAsync(async ctx => await ctx.Logs.CountAsync(l => l.Body == marker) == 3);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);
        var response = await client.GetFromJsonAsync<PagedLogsResponse>(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}", UriKind.Relative),
            JsonOptions);

        response.ShouldNotBeNull();
        var matching = response!.Items.Where(i => i.Body == marker).ToList();
        matching.Count.ShouldBe(3);
        matching.ShouldAllBe(i => i.SeverityText == "INFO");
        // Ordering: descending time
        matching.Select(i => i.Time).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task GetLogs_Pagination_Yields_Cursor_And_Completes()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var marker = $"query-paging-{Guid.NewGuid():N}";
        await SeedLogsAsync(client, anchor, marker, count: 3);
        await WaitForAsync(async ctx => await ctx.Logs.CountAsync(l => l.Body == marker) == 3);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);

        var first = await client.GetFromJsonAsync<PagedLogsResponse>(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&limit=2", UriKind.Relative),
            JsonOptions);
        first.ShouldNotBeNull();
        first!.Items.Count.ShouldBe(2);
        first.NextCursor.ShouldNotBeNullOrEmpty();

        var second = await client.GetFromJsonAsync<PagedLogsResponse>(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}",
                UriKind.Relative),
            JsonOptions);
        second.ShouldNotBeNull();
        var collected = first.Items.Concat(second!.Items).Where(i => i.Body == marker).ToList();
        collected.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetLogs_Invalid_Cursor_Returns_400()
    {
        using var client = _fixture.CreateClient();
        var from = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        using var response = await client.GetAsync(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&cursor=!!!", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetLogs_FilterByTraceId_ReturnsOnlyCorrelatedLogs()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 6, 10, 9, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var markerA = $"corr-A-{suffix}";
        var markerB = $"corr-B-{suffix}";
        var traceIdA = TraceId.FromBytes(RandomBytes(16));
        var traceIdB = TraceId.FromBytes(RandomBytes(16));

        await SeedLogsWithTraceAsync(client, anchor, markerA, traceIdA, count: 3);
        await SeedLogsWithTraceAsync(client, anchor.AddSeconds(10), markerB, traceIdB, count: 2);

        await WaitForAsync(async ctx =>
            await ctx.Logs.CountAsync(l => l.Body == markerA) == 3 &&
            await ctx.Logs.CountAsync(l => l.Body == markerB) == 2);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);

        var filtered = await client.GetFromJsonAsync<PagedLogsResponse>(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&traceId={traceIdA}&limit=100", UriKind.Relative),
            JsonOptions);

        filtered.ShouldNotBeNull();
        var matching = filtered!.Items.Where(i => i.Body == markerA || i.Body == markerB).ToList();
        matching.Count.ShouldBe(3);
        matching.ShouldAllBe(i => i.Body == markerA);
    }

    [Fact]
    public async Task GetLogs_MalformedTraceId_Returns400()
    {
        using var client = _fixture.CreateClient();
        var from = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddHours(1);

        using var response = await client.GetAsync(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&traceId=not-hex", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTraces_Returns_Summaries_Grouped_By_Trace()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 4, 10, 9, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var service = $"query-svc-{suffix}";
        var traceIdBytes1 = RandomBytes(16);
        var traceIdBytes2 = RandomBytes(16);
        var traceId1 = TraceId.FromBytes(traceIdBytes1);
        var traceId2 = TraceId.FromBytes(traceIdBytes2);
        var rootName1 = $"root.alpha.{suffix}";
        var rootName2 = $"root.beta.{suffix}";

        await SeedSpansAsync(client, service, anchor, traceIdBytes1, rootName1, spanCount: 3);
        await SeedSpansAsync(client, service, anchor.AddSeconds(10), traceIdBytes2, rootName2, spanCount: 2);

        await WaitForAsync(async ctx =>
            await ctx.Spans.CountAsync(s => s.TraceId == traceId1) == 3 &&
            await ctx.Spans.CountAsync(s => s.TraceId == traceId2) == 2);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);
        var response = await client.GetFromJsonAsync<PagedTracesResponse>(
            new Uri($"/api/v1/traces?from={Iso(from)}&to={Iso(to)}", UriKind.Relative),
            JsonOptions);

        response.ShouldNotBeNull();
        var ourIds = new[] { traceId1.ToString(), traceId2.ToString() };
        var ours = response!.Items.Where(t => ourIds.Contains(t.TraceId)).ToList();
        ours.Count.ShouldBe(2);
        ours.ShouldContain(t => t.TraceId == traceId1.ToString() && t.SpanCount == 3 && t.RootSpanName == rootName1);
        ours.ShouldContain(t => t.TraceId == traceId2.ToString() && t.SpanCount == 2 && t.RootSpanName == rootName2);
    }

    [Fact]
    public async Task GetTrace_Returns_All_Spans_For_Trace()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 5, 10, 9, 0, 0, TimeSpan.Zero);
        var service = $"detail-svc-{Guid.NewGuid():N}";
        var traceIdBytes = RandomBytes(16);
        await SeedSpansAsync(client, service, anchor, traceIdBytes, "detail.root", spanCount: 4);

        var traceId = TraceId.FromBytes(traceIdBytes);
        await WaitForAsync(async ctx => await ctx.Spans.CountAsync(s => s.TraceId == traceId) == 4);

        var response = await client.GetFromJsonAsync<TraceDetailResponse>(
            new Uri($"/api/v1/traces/{traceId}", UriKind.Relative),
            JsonOptions);

        response.ShouldNotBeNull();
        response!.TraceId.ShouldBe(traceId.ToString());
        response.Spans.Count.ShouldBe(4);
    }

    [Fact]
    public async Task GetTrace_Malformed_Id_Returns_400()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/traces/not-hex", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetTrace_Unknown_Id_Returns_404()
    {
        using var client = _fixture.CreateClient();
        var unknown = TraceId.FromBytes(RandomBytes(16));

        using var response = await client.GetAsync(new Uri($"/api/v1/traces/{unknown}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListMetrics_Returns_Seeded_Instrument()
    {
        using var client = _fixture.CreateClient();
        var metricName = $"cpu.load.{Guid.NewGuid():N}";
        await SeedGaugeAsync(client, metricName, anchorNano: 1_000_000_000L, value: 7.5);
        await WaitForInstrumentAsync(metricName);

        var response = await client.GetFromJsonAsync<IReadOnlyList<InstrumentItem>>(
            new Uri("/api/v1/metrics", UriKind.Relative), JsonOptions);

        response.ShouldNotBeNull();
        var match = response!.FirstOrDefault(i => i.Name == metricName);
        match.ShouldNotBeNull();
        match!.Kind.ShouldBe("Gauge");
        match.ScopeName.ShouldBe("tests");
        match.PointCount.ShouldBe(1);
        match.ResourceHash.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetMetricPoints_Returns_Series_For_Known_Instrument()
    {
        using var client = _fixture.CreateClient();
        var metricName = $"memory.bytes.{Guid.NewGuid():N}";
        var anchor = new DateTimeOffset(2030, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var anchorNano = UnixNanoTime.ToUnixNanoseconds(anchor);
        await SeedGaugeAsync(client, metricName, anchorNano, value: 1024d);
        var key = await WaitForInstrumentAsync(metricName);

        var url = $"/api/v1/metrics/points?resourceHash={key.ResourceHashHex}&scopeName={Uri.EscapeDataString(key.ScopeName)}&instrumentName={Uri.EscapeDataString(metricName)}&kind=Gauge";
        var response = await client.GetFromJsonAsync<MetricSeriesResponse>(new Uri(url, UriKind.Relative), JsonOptions);

        response.ShouldNotBeNull();
        response!.Instrument.Name.ShouldBe(metricName);
        response.Instrument.Kind.ShouldBe("Gauge");
        response.Points.Count.ShouldBe(1);
        response.Points[0].Value.ShouldBe(1024d);
    }

    [Fact]
    public async Task GetMetricPoints_Unknown_Instrument_Returns_404()
    {
        using var client = _fixture.CreateClient();
        // Well-formed hex, but guaranteed to not match any existing resource.
        var bogusHash = string.Concat(Enumerable.Repeat("ab", 32));
        var url = $"/api/v1/metrics/points?resourceHash={bogusHash}&scopeName=tests&instrumentName=nope&kind=Gauge";

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetMetricPoints_Missing_Required_Params_Returns_400()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(
            new Uri("/api/v1/metrics/points", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMetricPoints_Malformed_Kind_Returns_400()
    {
        using var client = _fixture.CreateClient();
        var bogusHash = string.Concat(Enumerable.Repeat("ab", 32));
        var url = $"/api/v1/metrics/points?resourceHash={bogusHash}&scopeName=tests&instrumentName=x&kind=NotARealKind";

        using var response = await client.GetAsync(new Uri(url, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMetricPoints_Filters_By_Time_Window()
    {
        using var client = _fixture.CreateClient();
        var metricName = $"rps.{Guid.NewGuid():N}";
        var anchor = new DateTimeOffset(2030, 8, 1, 12, 0, 0, TimeSpan.Zero);

        var request = new ExportMetricsServiceRequest();
        var resourceMetrics = new ResourceMetrics
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = $"svc-{metricName}" } },
                },
            },
        };
        var scope = new ScopeMetrics { Scope = new InstrumentationScope { Name = "tests" } };
        var gauge = new Gauge();
        // Three points at t=0, +1min, +2min from the anchor.
        for (var i = 0; i < 3; i++)
        {
            gauge.DataPoints.Add(new NumberDataPoint
            {
                TimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddMinutes(i)),
                AsDouble = i + 1,
            });
        }
        scope.Metrics.Add(new Metric { Name = metricName, Gauge = gauge });
        resourceMetrics.ScopeMetrics.Add(scope);
        request.ResourceMetrics.Add(resourceMetrics);

        using (var postResp = await PostProtobufAsync(client, "/v1/metrics", request))
        {
            postResp.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var key = await WaitForInstrumentAsync(metricName, expectedPoints: 3);

        // Window covers only the middle point.
        var from = anchor.AddSeconds(30);
        var to = anchor.AddSeconds(90);
        var url = $"/api/v1/metrics/points?resourceHash={key.ResourceHashHex}&scopeName=tests&instrumentName={Uri.EscapeDataString(metricName)}&kind=Gauge&from={Iso(from)}&to={Iso(to)}";
        var filtered = await client.GetFromJsonAsync<MetricSeriesResponse>(new Uri(url, UriKind.Relative), JsonOptions);

        filtered.ShouldNotBeNull();
        filtered!.Points.Count.ShouldBe(1);
        filtered.Points[0].Value.ShouldBe(2d);
    }

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    private static async Task SeedLogsAsync(
        HttpClient client,
        DateTimeOffset anchor,
        string marker,
        int count)
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = $"logs-{marker}" } },
                },
            },
        };
        var scopeLogs = new ScopeLogs { Scope = new InstrumentationScope { Name = "tests" } };

        for (var i = 0; i < count; i++)
        {
            scopeLogs.LogRecords.Add(new OtlpLogRecord
            {
                TimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddSeconds(i)),
                SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber.Info,
                SeverityText = "INFO",
                Body = new AnyValue { StringValue = marker },
            });
        }

        resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);

        using var response = await PostProtobufAsync(client, "/v1/logs", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task SeedLogsWithTraceAsync(
        HttpClient client,
        DateTimeOffset anchor,
        string marker,
        TraceId traceId,
        int count)
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = $"logs-{marker}" } },
                },
            },
        };
        var scopeLogs = new ScopeLogs { Scope = new InstrumentationScope { Name = "tests" } };
        var traceIdBytes = ByteString.CopyFrom(traceId.ToByteArray());

        for (var i = 0; i < count; i++)
        {
            scopeLogs.LogRecords.Add(new OtlpLogRecord
            {
                TimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddSeconds(i)),
                SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber.Info,
                SeverityText = "INFO",
                Body = new AnyValue { StringValue = marker },
                TraceId = traceIdBytes,
                SpanId = ByteString.CopyFrom(RandomBytes(8)),
            });
        }

        resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);

        using var response = await PostProtobufAsync(client, "/v1/logs", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task SeedSpansAsync(
        HttpClient client,
        string service,
        DateTimeOffset anchor,
        byte[] traceIdBytes,
        string rootName,
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
            Name = rootName,
            Kind = OtlpSpan.Types.SpanKind.Server,
            StartTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor),
            EndTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddMilliseconds(100)),
            Status = new Status { Code = Status.Types.StatusCode.Ok },
        });

        for (var i = 1; i < spanCount; i++)
        {
            scopeSpans.Spans.Add(new OtlpSpan
            {
                TraceId = ByteString.CopyFrom(traceIdBytes),
                SpanId = ByteString.CopyFrom(RandomBytes(8)),
                ParentSpanId = ByteString.CopyFrom(rootSpanId),
                Name = $"{rootName}.child.{i}",
                Kind = OtlpSpan.Types.SpanKind.Internal,
                StartTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddMilliseconds(i * 10)),
                EndTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddMilliseconds(i * 10 + 20)),
                Status = new Status { Code = Status.Types.StatusCode.Ok },
            });
        }

        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);

        using var response = await PostProtobufAsync(client, "/v1/traces", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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

    private static async Task SeedGaugeAsync(
        HttpClient client,
        string metricName,
        long anchorNano,
        double value)
    {
        var request = new ExportMetricsServiceRequest();
        var resourceMetrics = new ResourceMetrics
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = $"svc-{metricName}" } },
                },
            },
        };
        var scope = new ScopeMetrics { Scope = new InstrumentationScope { Name = "tests" } };
        scope.Metrics.Add(new Metric
        {
            Name = metricName,
            Unit = "unit",
            Gauge = new Gauge
            {
                DataPoints =
                {
                    new NumberDataPoint
                    {
                        TimeUnixNano = (ulong)anchorNano,
                        AsDouble = value,
                    },
                },
            },
        });
        resourceMetrics.ScopeMetrics.Add(scope);
        request.ResourceMetrics.Add(resourceMetrics);

        using var response = await PostProtobufAsync(client, "/v1/metrics", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLogs_FiltersByServiceName()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var markerA = $"svcfilter-A-{suffix}";
        var markerB = $"svcfilter-B-{suffix}";
        var serviceA = $"svc-{markerA}";
        var serviceB = $"svc-{markerB}";

        await SeedLogsAsync(client, anchor, markerA, count: 3);
        await SeedLogsAsync(client, anchor.AddSeconds(10), markerB, count: 2);
        await WaitForAsync(async ctx =>
            await ctx.Logs.CountAsync(l => l.Body == markerA) == 3 &&
            await ctx.Logs.CountAsync(l => l.Body == markerB) == 2);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);

        var filtered = await client.GetFromJsonAsync<PagedLogsResponse>(
            new Uri($"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&service={Uri.EscapeDataString($"logs-{markerA}")}&limit=100", UriKind.Relative),
            JsonOptions);

        filtered.ShouldNotBeNull();
        var relevant = filtered!.Items.Where(i => i.Body == markerA || i.Body == markerB).ToList();
        relevant.Count.ShouldBe(3);
        relevant.ShouldAllBe(i => i.Body == markerA);
        relevant.ShouldAllBe(i => i.ServiceName == $"logs-{markerA}");
    }

    [Fact]
    public async Task GetLogs_Services_Returns_Distinct_Sorted()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var markerA = $"svclist-A-{suffix}";
        var markerB = $"svclist-B-{suffix}";

        await SeedLogsAsync(client, anchor, markerA, count: 1);
        await SeedLogsAsync(client, anchor.AddSeconds(5), markerB, count: 1);
        await WaitForAsync(async ctx =>
            await ctx.Logs.CountAsync(l => l.Body == markerA) == 1 &&
            await ctx.Logs.CountAsync(l => l.Body == markerB) == 1);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);

        var services = await client.GetFromJsonAsync<string[]>(
            new Uri($"/api/v1/logs/services?from={Iso(from)}&to={Iso(to)}", UriKind.Relative),
            JsonOptions);

        services.ShouldNotBeNull();
        var ours = services!.Where(s => s.Contains(suffix, StringComparison.Ordinal)).ToList();
        ours.Count.ShouldBe(2);
        ours.ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public async Task GetTraces_FiltersByServiceName_AnyServiceInTrace()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var serviceA = $"trace-svc-A-{suffix}";
        var serviceB = $"trace-svc-B-{suffix}";
        var traceIdBytesA = RandomBytes(16);
        var traceIdBytesB = RandomBytes(16);

        await SeedSpansAsync(client, serviceA, anchor, traceIdBytesA, $"a.root.{suffix}", spanCount: 2);
        await SeedSpansAsync(client, serviceB, anchor.AddSeconds(5), traceIdBytesB, $"b.root.{suffix}", spanCount: 2);
        var traceIdA = TraceId.FromBytes(traceIdBytesA);
        var traceIdB = TraceId.FromBytes(traceIdBytesB);
        await WaitForAsync(async ctx =>
            await ctx.Spans.CountAsync(s => s.TraceId == traceIdA) == 2 &&
            await ctx.Spans.CountAsync(s => s.TraceId == traceIdB) == 2);

        var from = anchor.AddMinutes(-5);
        var to = anchor.AddMinutes(5);

        var filtered = await client.GetFromJsonAsync<PagedTracesResponse>(
            new Uri($"/api/v1/traces?from={Iso(from)}&to={Iso(to)}&service={Uri.EscapeDataString(serviceA)}", UriKind.Relative),
            JsonOptions);

        filtered.ShouldNotBeNull();
        var ourIds = new[] { traceIdA.ToString(), traceIdB.ToString() };
        var ours = filtered!.Items.Where(t => ourIds.Contains(t.TraceId)).ToList();
        ours.Count.ShouldBe(1);
        ours[0].TraceId.ShouldBe(traceIdA.ToString());
        ours[0].ServiceName.ShouldBe(serviceA);
    }

    [Fact]
    public async Task GetTrace_Detail_Carries_ServiceName_PerSpan()
    {
        using var client = _fixture.CreateClient();

        var anchor = new DateTimeOffset(2030, 12, 1, 12, 0, 0, TimeSpan.Zero);
        var suffix = Guid.NewGuid().ToString("N");
        var serviceA = $"multi-A-{suffix}";
        var serviceB = $"multi-B-{suffix}";
        var traceIdBytes = RandomBytes(16);
        // First push root from service A...
        await SeedSpansAsync(client, serviceA, anchor, traceIdBytes, $"a.root.{suffix}", spanCount: 1);
        // ...then an extra span from service B on the SAME trace.
        await SeedExtraSpanAsync(client, serviceB, anchor.AddMilliseconds(50), traceIdBytes, $"b.child.{suffix}");

        var traceId = TraceId.FromBytes(traceIdBytes);
        await WaitForAsync(async ctx => await ctx.Spans.CountAsync(s => s.TraceId == traceId) == 2);

        var detail = await client.GetFromJsonAsync<TraceDetailResponse>(
            new Uri($"/api/v1/traces/{traceId}", UriKind.Relative),
            JsonOptions);

        detail.ShouldNotBeNull();
        detail!.Spans.Count.ShouldBe(2);
        detail.Spans.Select(s => s.ServiceName).OrderBy(s => s).ShouldBe([serviceA, serviceB]);
    }

    [Fact]
    public async Task GetMetrics_Services_Returns_Distinct()
    {
        using var client = _fixture.CreateClient();
        var anchor = new DateTimeOffset(2031, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var anchorNano = UnixNanoTime.ToUnixNanoseconds(anchor);

        var metricA = $"svc-metrics-{Guid.NewGuid():N}";
        await SeedGaugeAsync(client, metricA, anchorNano, 1.0);
        await WaitForInstrumentAsync(metricA);

        var services = await client.GetFromJsonAsync<string[]>(
            new Uri("/api/v1/metrics/services", UriKind.Relative), JsonOptions);

        services.ShouldNotBeNull();
        services!.ShouldContain($"svc-{metricA}");
    }

    private static async Task SeedExtraSpanAsync(
        HttpClient client,
        string service,
        DateTimeOffset anchor,
        byte[] traceIdBytes,
        string spanName)
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
        scopeSpans.Spans.Add(new OtlpSpan
        {
            TraceId = ByteString.CopyFrom(traceIdBytes),
            SpanId = ByteString.CopyFrom(RandomBytes(8)),
            Name = spanName,
            Kind = OtlpSpan.Types.SpanKind.Internal,
            StartTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor),
            EndTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(anchor.AddMilliseconds(20)),
            Status = new Status { Code = Status.Types.StatusCode.Ok },
        });
        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);

        using var response = await PostProtobufAsync(client, "/v1/traces", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<InstrumentKey> WaitForInstrumentAsync(string instrumentName, int expectedPoints = 1, int timeoutSeconds = 10)
    {
        var reader = _fixture.Services.GetRequiredService<IMetricReader>();
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var summaries = await reader.ListInstrumentsAsync(CancellationToken.None);
            var match = summaries.FirstOrDefault(s => s.Key.InstrumentName == instrumentName);
            if (match is { } found && found.PointCount >= expectedPoints)
            {
                return found.Key;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Instrument '{instrumentName}' did not reach {expectedPoints} point(s) in time.");
    }

    private sealed record PagedLogsResponse(IReadOnlyList<LogItem> Items, string? NextCursor);
    private sealed record LogItem(DateTimeOffset Time, string? Body, string? SeverityText, string? ServiceName);

    private sealed record PagedTracesResponse(IReadOnlyList<TraceItem> Items, string? NextCursor);
    private sealed record TraceItem(string TraceId, string RootSpanName, int SpanCount, string? ServiceName);

    private sealed record TraceDetailResponse(string TraceId, IReadOnlyList<SpanItem> Spans);
    private sealed record SpanItem(string SpanId, string Name, string? ServiceName);

    private sealed record InstrumentItem(
        string ResourceHash,
        string? ServiceName,
        string ScopeName,
        string Name,
        string Kind,
        string? Description,
        string? Unit,
        bool IsMonotonic,
        string Temporality,
        int PointCount);

    private sealed record MetricSeriesResponse(InstrumentItem Instrument, IReadOnlyList<MetricPointItem> Points);
    private sealed record MetricPointItem(DateTimeOffset Time, DateTimeOffset StartTime, double Value);
}
