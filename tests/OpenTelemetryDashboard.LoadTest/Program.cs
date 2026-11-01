using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenTelemetryDashboard.Benchmarks;
using OpenTelemetryDashboard.LoadTest;

var options = LoadOptions.Parse(args);

using var http = new HttpClient { BaseAddress = options.HttpEndpoint, Timeout = TimeSpan.FromSeconds(60) };
if (!string.IsNullOrEmpty(options.BrowserToken))
{
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.BrowserToken);
}

await WaitForHealthyAsync(http);

using var client = new OtlpGrpcClient(options.GrpcEndpoint, options.ApiKey);
var sampler = options.Pid is int pid ? new ProcessSampler(pid) : null;
sampler?.Sample();
await Task.Delay(1000);
var idle = sampler?.Sample();

var probe = new TraceProbe(client, http, options.Services);
var startedAt = DateTimeOffset.UtcNow;
IngestResult? ingest = null;
double? drainSeconds = null;
if (!options.SkipIngest)
{
    Console.WriteLine($"Ingesting {string.Join('+', options.Signals)} for {options.DurationSeconds}s with {options.Workers} workers, {options.BatchSize} items per request...");
    ingest = await new IngestRunner(options, client, probe, sampler).RunAsync();

    Console.WriteLine("Waiting for the writer to drain...");
    var drainWatch = Stopwatch.StartNew();
    var drained = await probe.MeasureAsync(TimeSpan.FromMinutes(10));
    drainSeconds = drained is null ? null : Math.Round(drainWatch.Elapsed.TotalSeconds, 2);
}
var afterIngest = sampler?.Sample();

var acceptedItems = ingest?.Signals.Values.Sum(s => s.AcceptedItems) ?? 0;
var persistedPerSecond = ingest is not null && drainSeconds is double d ? Math.Round(acceptedItems / (ingest.SendSeconds + d), 0) : (double?)null;

List<QueryResult> queries = [];
if (!options.SkipQuery)
{
    Console.WriteLine("Running queries over the last hour, the SPA default window...");
    var serviceNames = new OtlpPayloadFactory(seed: 0, options.Services).ServiceNames;
    var now = DateTimeOffset.UtcNow;
    queries = await new QueryRunner(http, options.QueryIterations, serviceNames)
        .RunAsync(now.AddHours(-1), now);
}
var afterQueries = sampler?.Sample();

var result = new
{
    options.Label,
    StartedAt = startedAt,
    Machine = new
    {
        Os = RuntimeInformation.OSDescription,
        Cpu = Environment.ProcessorCount,
        LoadGeneratorRuntime = RuntimeInformation.FrameworkDescription,
    },
    Options = options,
    Ingest = ingest,
    DrainSeconds = drainSeconds,
    PersistedItemsPerSecond = persistedPerSecond,
    Memory = new { IdleRssMb = idle?.RssMb, AfterIngestRssMb = afterIngest?.RssMb, AfterQueriesRssMb = afterQueries?.RssMb },
    Queries = queries,
};

Directory.CreateDirectory(options.OutputDirectory);
var file = Path.Combine(options.OutputDirectory, $"{startedAt:yyyyMMdd-HHmmss}-{options.Label}.json");
await File.WriteAllTextAsync(file, JsonSerializer.Serialize(result, JsonOutput.Options));

if (ingest is not null)
{
    Console.WriteLine();
    Console.WriteLine($"{"signal",-10} {"items/s",10} {"accepted",12} {"rejected",9} {"failed",7} {"p50 ms",8} {"p95 ms",8} {"p99 ms",8}");
    foreach (var (signal, s) in ingest.Signals)
    {
        Console.WriteLine($"{signal,-10} {s.ItemsPerSecond,10:N0} {s.AcceptedItems,12:N0} {s.RejectedRequests,9:N0} {s.FailedRequests,7:N0} {s.ExportLatencyMs.P50,8:N1} {s.ExportLatencyMs.P95,8:N1} {s.ExportLatencyMs.P99,8:N1}");
        if (s.LastError is not null) Console.WriteLine($"  last error: {s.LastError}");
    }
    Console.WriteLine();
    Console.WriteLine($"Accepted total: {acceptedItems:N0} items in {ingest.SendSeconds:N1}s, drain {drainSeconds?.ToString("N1", System.Globalization.CultureInfo.InvariantCulture) ?? "timeout"}s, persisted {persistedPerSecond:N0} items/s");
    Console.WriteLine($"Freshness (ingest to query): p50 {ingest.FreshnessMs.P50:N0} ms, p95 {ingest.FreshnessMs.P95:N0} ms, samples {ingest.FreshnessMs.Count}");
}
if (sampler is not null && ingest is not null)
{
    var peak = ingest.Timeline.Where(t => t.RssMb is not null).Select(t => t.RssMb!.Value).DefaultIfEmpty().Max();
    var avgCpu = ingest.Timeline.Where(t => t.CpuPercent is not null).Select(t => t.CpuPercent!.Value).DefaultIfEmpty().Average();
    Console.WriteLine($"Memory: idle {idle?.RssMb:N0} MB, peak {peak:N0} MB, after queries {afterQueries?.RssMb:N0} MB; avg CPU {avgCpu:N0}% (100% = 1 core)");
}
if (queries.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine($"{"query",-28} {"p50 ms",8} {"p95 ms",8} {"max ms",8} {"failed",7}");
    foreach (var q in queries)
    {
        Console.WriteLine($"{q.Name,-28} {q.LatencyMs.P50,8:N1} {q.LatencyMs.P95,8:N1} {q.LatencyMs.Max,8:N1} {q.Failures,7}{(q.LastFailureStatus is null ? "" : $" ({q.LastFailureStatus})")}");
    }
}
Console.WriteLine();
Console.WriteLine($"Results written to {file}");

static async Task WaitForHealthyAsync(HttpClient http)
{
    var deadline = DateTime.UtcNow.AddSeconds(60);
    while (true)
    {
        try
        {
            using var response = await http.GetAsync("/healthz");
            if ((int)response.StatusCode < 500) return;
        }
        catch (HttpRequestException) when (DateTime.UtcNow < deadline)
        {
        }
        if (DateTime.UtcNow >= deadline) throw new TimeoutException("Dashboard did not become reachable at /healthz.");
        await Task.Delay(500);
    }
}

internal static class JsonOutput
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}
