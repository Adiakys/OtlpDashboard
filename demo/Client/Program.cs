using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string serviceName = "sample-client";
const string serviceNamespace = "oteldemo";
const string activitySourceName = "SampleClient";
const string meterName = "SampleClient";

var builder = Host.CreateApplicationBuilder(args);

// Server endpoints — accepts either the CSV plural `Server:BaseUrls`
// for the multi-instance demo (round-robin across them per iteration)
// or the singular `Server:BaseUrl` for the legacy single-server case.
// Both shapes are kept so older compose files / dev setups keep
// working unchanged.
var serverBaseUrls = (builder.Configuration["Server:BaseUrls"]
        ?? builder.Configuration["Server:BaseUrl"]
        ?? "http://sample-server:8080")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (serverBaseUrls.Length == 0)
{
    throw new InvalidOperationException("At least one Server:BaseUrl(s) entry is required.");
}
var otlpEndpoint = builder.Configuration["Otel:Endpoint"]
    ?? "http://localhost:4317";
var otlpHeaders = builder.Configuration["Otel:Headers"]
    ?? string.Empty;
var loopIntervalMs = builder.Configuration.GetValue("Loop:IntervalMs", 3000);
// Stable `service.instance.id` so widgets pinned to this instance survive
// container restarts. The OTel SDK otherwise generates a fresh GUID on
// every boot.
var serviceInstanceId = builder.Configuration["Otel:ServiceInstanceId"]
    ?? "client-1";

// One named HttpClient per server URL. Names are zero-indexed
// (`server-0`, `server-1`, …) so the ticker can round-robin by
// modulo without parsing URLs back out. The shared 5s timeout
// lives on every named client.
for (var i = 0; i < serverBaseUrls.Length; i++)
{
    var url = serverBaseUrls[i];
    builder.Services.AddHttpClient($"server-{i}", c =>
    {
        c.BaseAddress = new Uri(url);
        c.Timeout = TimeSpan.FromSeconds(5);
    });
}

void ConfigureOtlp(OpenTelemetry.Exporter.OtlpExporterOptions opt)
{
    opt.Endpoint = new Uri(otlpEndpoint);
    if (!string.IsNullOrWhiteSpace(otlpHeaders))
    {
        opt.Headers = otlpHeaders;
    }
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName, serviceNamespace, "1.0.0",
            autoGenerateServiceInstanceId: false,
            serviceInstanceId: serviceInstanceId)
        .AddAttributes([new KeyValuePair<string, object>("deployment.environment", "demo")]))
    .WithTracing(t => t
        .AddSource(activitySourceName)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(ConfigureOtlp))
    .WithMetrics(m => m
        .AddMeter(meterName)
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(ConfigureOtlp));

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.AddOtlpExporter(ConfigureOtlp);
});

builder.Services.Configure<HostOptions>(opt =>
{
    opt.ShutdownTimeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddSingleton(new TickerOptions(loopIntervalMs, serverBaseUrls));
builder.Services.AddHostedService<TickerService>();

var host = builder.Build();
await host.RunAsync();

internal sealed record TickerOptions(int IntervalMs, string[] ServerBaseUrls);

internal sealed class TickerService(
    IHttpClientFactory httpFactory,
    TickerOptions options,
    ILogger<TickerService> logger) : BackgroundService
{
    private static readonly ActivitySource Activity = new("SampleClient");
    private static readonly Meter Meter = new("SampleClient", "1.0.0");

    private readonly Counter<long> _iterations = Meter.CreateCounter<long>(
        "sample_client.iterations", unit: "1",
        description: "Number of client loop iterations completed.");
    private readonly Histogram<double> _iterationLatency = Meter.CreateHistogram<double>(
        "sample_client.iteration_latency", unit: "ms",
        description: "End-to-end latency of one client iteration.");

    private long _tick;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromMilliseconds(options.IntervalMs);
        using var timer = new PeriodicTimer(period);

        // Health-check every configured server once at startup so the
        // logs make it obvious all instances are up before the loop
        // begins distributing iterations across them.
        for (var i = 0; i < options.ServerBaseUrls.Length; i++)
        {
            await TryHealthCheck(httpFactory.CreateClient($"server-{i}"), options.ServerBaseUrls[i], stoppingToken);
        }

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // Round-robin per iteration so successive ticks hit
            // different instances; the modulo keeps the rotation
            // stable across the lifetime of the process.
            var index = (int)(Interlocked.Increment(ref _tick) % options.ServerBaseUrls.Length);
            var http = httpFactory.CreateClient($"server-{index}");
            await RunIteration(http, options.ServerBaseUrls[index], stoppingToken);
        }
    }

    private async Task RunIteration(HttpClient http, string serverUrl, CancellationToken ct)
    {
        using var act = Activity.StartActivity("client.iteration");
        var sw = Stopwatch.StartNew();
        var op = PickOp();
        act?.SetTag("op", op);
        act?.SetTag("peer.url", serverUrl);
        try
        {
            switch (op)
            {
                case "read":
                    var read = await http.GetFromJsonAsync<CounterResponse>("/counter", ct);
                    logger.LogInformation("read counter={Value}", read?.Value);
                    break;
                case "random":
                {
                    using var resp = await http.PostAsync("/counter/random", content: null, ct);
                    resp.EnsureSuccessStatusCode();
                    var body = await resp.Content.ReadFromJsonAsync<RandomResponse>(ct);
                    logger.LogInformation("random delta={Delta} new={New}", body?.Delta, body?.NewValue);
                    break;
                }
                case "set":
                {
                    var newValue = Random.Shared.Next(0, 1000);
                    using var resp = await http.PostAsync($"/counter/{newValue}", content: null, ct);
                    resp.EnsureSuccessStatusCode();
                    logger.LogInformation("set counter={Value}", newValue);
                    break;
                }
            }
            _iterations.Add(1, new KeyValuePair<string, object?>("op", op));
        }
        catch (Exception ex)
        {
            act?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _iterations.Add(1,
                new KeyValuePair<string, object?>("op", op),
                new KeyValuePair<string, object?>("status", "error"));
            logger.LogWarning(ex, "iteration {Op} failed", op);
        }
        finally
        {
            _iterationLatency.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("op", op));
        }
    }

    private static string PickOp()
    {
        // 70% read, 25% random mutate, 5% explicit set — produces a
        // realistic-looking traffic mix without being all noise.
        var r = Random.Shared.NextDouble();
        if (r < 0.70) return "read";
        if (r < 0.95) return "random";
        return "set";
    }

    private async Task TryHealthCheck(HttpClient http, string serverUrl, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/healthz", ct);
            logger.LogInformation("healthz {Url} {Status}", serverUrl, resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "healthz {Url} failed; will keep retrying via the loop.", serverUrl);
        }
    }

    private sealed record CounterResponse(int Value);
    private sealed record RandomResponse(int OldValue, int NewValue, int Delta);
}
