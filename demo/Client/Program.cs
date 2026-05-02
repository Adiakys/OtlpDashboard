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

var serverBaseUrl = builder.Configuration["Server:BaseUrl"]
    ?? "http://sample-server:8080";
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

builder.Services.AddHttpClient("server", c =>
{
    c.BaseAddress = new Uri(serverBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(5);
});

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

builder.Services.AddSingleton(new TickerOptions(loopIntervalMs));
builder.Services.AddHostedService<TickerService>();

var host = builder.Build();
await host.RunAsync();

internal sealed record TickerOptions(int IntervalMs);

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var http = httpFactory.CreateClient("server");
        var period = TimeSpan.FromMilliseconds(options.IntervalMs);
        using var timer = new PeriodicTimer(period);

        await TryHealthCheck(http, stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunIteration(http, stoppingToken);
        }
    }

    private async Task RunIteration(HttpClient http, CancellationToken ct)
    {
        using var act = Activity.StartActivity("client.iteration");
        var sw = Stopwatch.StartNew();
        var op = PickOp();
        act?.SetTag("op", op);
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

    private async Task TryHealthCheck(HttpClient http, CancellationToken ct)
    {
        try
        {
            using var resp = await http.GetAsync("/healthz", ct);
            logger.LogInformation("healthz {Status}", resp.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "healthz failed; will keep retrying via the loop.");
        }
    }

    private sealed record CounterResponse(int Value);
    private sealed record RandomResponse(int OldValue, int NewValue, int Delta);
}
