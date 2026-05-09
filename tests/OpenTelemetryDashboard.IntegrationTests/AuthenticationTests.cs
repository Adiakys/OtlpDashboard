using System.Net;
using System.Net.Http.Headers;
using Google.Protobuf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Trace.V1;
using OpenTelemetryDashboard.Persistence;
using OtlpResource = OpenTelemetry.Proto.Resource.V1.Resource;
using OtlpSpan = OpenTelemetry.Proto.Trace.V1.Span;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// End-to-end checks of the static-token auth contract with both tokens
/// configured. Uses a dedicated <see cref="WebApplicationFactory{TEntryPoint}"/>
/// because the shared <see cref="TestHostFixture"/> deliberately leaves tokens
/// empty (opt-in posture preserved for other tests).
/// </summary>
public sealed class AuthenticationTests : IAsyncLifetime
{
    private const string BrowserToken = "browser-secret-x9q2";
    private const string OtlpApiKey = "otlp-secret-k7m3";
    private const string AppName = "Acme Observability";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"oteldash-auth-{Guid.NewGuid():N}.db");

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={_dbPath}",
                        ["Dashboard:BrowserToken"] = BrowserToken,
                        ["Dashboard:Otlp:ApiKey"] = OtlpApiKey,
                        ["Dashboard:ApplicationName"] = AppName,
                    });
                });
            });

        _ = _factory.Services;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        TempSqliteFiles.TryDelete(_dbPath);
    }

    private HttpClient Client() => _factory!.CreateClient();

    private static string LogsUrl() =>
        $"/api/v1/logs?from=2030-01-01T00:00:00Z&to=2030-01-01T01:00:00Z";

    [Fact]
    public async Task GetLogs_Without_Authorization_Returns_401()
    {
        using var client = Client();

        using var response = await client.GetAsync(new Uri(LogsUrl(), UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLogs_With_BrowserToken_Returns_200()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BrowserToken);

        using var response = await client.GetAsync(new Uri(LogsUrl(), UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetLogs_With_OtlpApiKey_Returns_403()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OtlpApiKey);

        using var response = await client.GetAsync(new Uri(LogsUrl(), UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostTraces_Without_Authorization_Returns_401()
    {
        using var client = Client();
        using var content = MinimalTraceRequestContent();

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTraces_With_OtlpApiKey_Returns_200()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", OtlpApiKey);
        using var content = MinimalTraceRequestContent();

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostTraces_With_BrowserToken_Returns_403()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BrowserToken);
        using var content = MinimalTraceRequestContent();

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostTraces_With_OtlpApiKeyHeader_Returns_200()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Add("x-otlp-api-key", OtlpApiKey);
        using var content = MinimalTraceRequestContent();

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PostTraces_With_Wrong_OtlpApiKeyHeader_Returns_401()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Add("x-otlp-api-key", "obviously-wrong");
        using var content = MinimalTraceRequestContent();

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTraces_With_BrowserToken_In_OtlpApiKeyHeader_Returns_401()
    {
        // The x-otlp-api-key header is OTLP-scoped: it must not grant ingest
        // access just because the value happens to match BrowserToken.
        using var client = Client();
        client.DefaultRequestHeaders.Add("x-otlp-api-key", BrowserToken);
        using var content = MinimalTraceRequestContent();

        using var response = await client.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLogs_With_OtlpApiKeyHeader_Returns_403()
    {
        // Authenticated as OTLP role, but the read-api policy requires browser.
        using var client = Client();
        client.DefaultRequestHeaders.Add("x-otlp-api-key", OtlpApiKey);

        using var response = await client.GetAsync(new Uri(LogsUrl(), UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Healthz_Is_Always_Public()
    {
        using var client = Client();

        using var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Info_Endpoint_Unauthenticated_Returns_Name_But_Version_Null()
    {
        using var client = Client();

        using var response = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"applicationName\"");
        body.ShouldContain(AppName);
        body.ShouldContain("\"version\":null");
    }

    [Fact]
    public async Task Info_Endpoint_Authenticated_Returns_Version()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BrowserToken);

        using var response = await client.GetAsync(new Uri("/api/v1/info", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"applicationName\"");
        body.ShouldContain(AppName);
        body.ShouldContain("\"version\"");
        body.ShouldNotContain("\"version\":null");
        body.ShouldNotContain("\"version\":\"unknown\"");
    }

    private static ByteArrayContent MinimalTraceRequestContent()
    {
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
                            new KeyValue { Key = "service.name", Value = new AnyValue { StringValue = "auth-test" } },
                        },
                    },
                    ScopeSpans =
                    {
                        new ScopeSpans
                        {
                            Scope = new InstrumentationScope { Name = "tests" },
                            Spans =
                            {
                                new OtlpSpan
                                {
                                    TraceId = ByteString.CopyFrom(RandomBytes(16)),
                                    SpanId = ByteString.CopyFrom(RandomBytes(8)),
                                    Name = $"auth-test.{Guid.NewGuid():N}",
                                    StartTimeUnixNano = 1,
                                    EndTimeUnixNano = 2,
                                },
                            },
                        },
                    },
                },
            },
        };

        var content = new ByteArrayContent(request.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return content;
    }

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        Random.Shared.NextBytes(b);
        return b;
    }
}
