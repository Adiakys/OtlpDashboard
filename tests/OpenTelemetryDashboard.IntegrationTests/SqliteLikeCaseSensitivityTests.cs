using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetryDashboard.Core.Common;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpLogRecord = OpenTelemetry.Proto.Logs.V1.LogRecord;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Documents that <c>EF.Functions.Like</c> on SQLite behaves case-insensitively
/// for ASCII out of the box — matching Postgres' <c>ILIKE</c> translation and
/// SQL Server's default collation. SQLite's docs assert the LIKE operator is
/// case-insensitive for ASCII unless <c>PRAGMA case_sensitive_like = ON</c>;
/// we don't set that pragma, so this test pins the cross-provider parity in
/// place against future regressions or contradictory audit findings.
/// </summary>
public sealed class SqliteLikeCaseSensitivityTests : IClassFixture<TestHostFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly TestHostFixture _fixture;

    public SqliteLikeCaseSensitivityTests(TestHostFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("hello")]
    [InlineData("HELLO")]
    [InlineData("Hello")]
    public async Task LogBody_Search_Is_Case_Insensitive(string searchTerm)
    {
        var marker = $"casetest-{Guid.NewGuid():N}";
        var anchor = DateTimeOffset.UtcNow.AddSeconds(-30);

        using var client = _fixture.CreateClient();
        await SeedLogAsync(client, anchor, marker, body: "Hello World " + marker);

        // The `bodyContains` filter is a substring match. We send the marker
        // suffix in three different cases — every variant must find the row.
        var pattern = $"{searchTerm} World {marker}";

        var from = anchor.AddMinutes(-1);
        var to = anchor.AddMinutes(1);

        await Eventually(async () =>
        {
            var response = await client.GetFromJsonAsync<PagedLogsResponse>(
                new Uri(
                    $"/api/v1/logs?from={Iso(from)}&to={Iso(to)}&bodyContains={Uri.EscapeDataString(pattern)}",
                    UriKind.Relative),
                JsonOptions);
            return response is { Items.Count: > 0 };
        });
    }

    [Theory]
    [InlineData("getuser")]
    [InlineData("GETUSER")]
    [InlineData("GetUser")]
    public async Task SpanName_Search_Is_Case_Insensitive(string searchTerm)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var spanName = $"GetUser-{marker}";
        var anchor = DateTimeOffset.UtcNow.AddSeconds(-30);

        using var client = _fixture.CreateClient();
        await SeedSpanAsync(client, anchor, spanName);

        var pattern = $"{searchTerm}-{marker}";

        var from = anchor.AddMinutes(-1);
        var to = anchor.AddMinutes(1);

        await Eventually(async () =>
        {
            var response = await client.GetFromJsonAsync<PagedTracesResponse>(
                new Uri(
                    $"/api/v1/traces?from={Iso(from)}&to={Iso(to)}&spanNameContains={Uri.EscapeDataString(pattern)}",
                    UriKind.Relative),
                JsonOptions);
            return response is { Items.Count: > 0 };
        });
    }

    private static async Task SeedLogAsync(HttpClient client, DateTimeOffset at, string serviceMarker, string body)
    {
        var request = new ExportLogsServiceRequest();
        var resourceLogs = new ResourceLogs
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = $"like-{serviceMarker}" } },
                },
            },
        };
        var scopeLogs = new ScopeLogs { Scope = new InstrumentationScope { Name = "tests" } };
        scopeLogs.LogRecords.Add(new OtlpLogRecord
        {
            TimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(at),
            SeverityNumber = OpenTelemetry.Proto.Logs.V1.SeverityNumber.Info,
            SeverityText = "INFO",
            Body = new AnyValue { StringValue = body },
        });
        resourceLogs.ScopeLogs.Add(scopeLogs);
        request.ResourceLogs.Add(resourceLogs);

        using var response = await PostProtobufAsync(client, "/v1/logs", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task SeedSpanAsync(HttpClient client, DateTimeOffset at, string spanName)
    {
        var request = new ExportTraceServiceRequest();
        var resourceSpans = new ResourceSpans
        {
            Resource = new OtlpResource
            {
                Attributes =
                {
                    new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "like-trace" } },
                },
            },
        };
        var scopeSpans = new ScopeSpans { Scope = new InstrumentationScope { Name = "tests" } };
        scopeSpans.Spans.Add(new OtlpSpan
        {
            Name = spanName,
            TraceId = ByteString.CopyFrom(RandomBytes(16)),
            SpanId = ByteString.CopyFrom(RandomBytes(8)),
            StartTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(at),
            EndTimeUnixNano = (ulong)UnixNanoTime.ToUnixNanoseconds(at.AddMilliseconds(50)),
            Kind = OtlpSpan.Types.SpanKind.Internal,
        });
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

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Polls until the predicate returns true or the timeout
    /// elapses — the ingest channel is async, so a write right after the
    /// POST won't be queryable for a few millis.</summary>
    private static async Task Eventually(Func<Task<bool>> predicate, int timeoutSeconds = 5)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(100);
        }
        throw new TimeoutException("Expected row did not appear within timeout — case-insensitive search may have regressed.");
    }

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        Random.Shared.NextBytes(b);
        return b;
    }

    private sealed record PagedLogsResponse(IReadOnlyList<object> Items, string? NextCursor);
    private sealed record PagedTracesResponse(IReadOnlyList<object> Items, string? NextCursor);
}
