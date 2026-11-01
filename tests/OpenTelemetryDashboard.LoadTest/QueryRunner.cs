using System.Diagnostics;
using System.Text.Json;

namespace OpenTelemetryDashboard.LoadTest;

/// <summary>
/// Replays the read queries the SPA issues on its main screens and records
/// their latency against the data the ingest phase left behind.
/// </summary>
internal sealed class QueryRunner(HttpClient http, int iterations, IReadOnlyList<string> serviceNames)
{
    public async Task<List<QueryResult>> RunAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var window = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        var service = serviceNames[serviceNames.Count / 2];

        var queries = new List<(string Name, string Path)>
        {
            ("traces.list", $"/api/v1/traces?{window}&limit=100"),
            ("traces.byService", $"/api/v1/traces?{window}&limit=100&services={service}"),
            ("traces.errors", $"/api/v1/traces?{window}&limit=100&status=error"),
            ("traces.slow", $"/api/v1/traces?{window}&limit=100&minMs=100"),
            ("traces.byAttribute", $"/api/v1/traces?{window}&limit=100&attr=http.route:/api/orders"),
            ("traces.spanNameContains", $"/api/v1/traces?{window}&limit=100&spanNameContains=checkout"),
            ("traces.aggregations.count", $"/api/v1/traces/aggregations?{window}&metric=count"),
            ("traces.aggregations.avgMs", $"/api/v1/traces/aggregations?{window}&metric=avgMs"),
            ("traces.services", $"/api/v1/traces/services?{window}"),
            ("logs.list", $"/api/v1/logs?{window}&limit=100"),
            ("logs.errors", $"/api/v1/logs?{window}&limit=100&minSeverity=17"),
            ("logs.bodyContains", $"/api/v1/logs?{window}&limit=100&bodyContains=Cache%20miss"),
            ("logs.byService", $"/api/v1/logs?{window}&limit=100&services={service}"),
            ("logs.services", $"/api/v1/logs/services?{window}"),
            ("metrics.instruments", $"/api/v1/metrics?{window}"),
            ("serviceMap", $"/api/v1/service-map?{window}"),
        };

        var traceId = await FirstStringAsync($"/api/v1/traces?{window}&limit=1", "items", "traceId");
        if (traceId is not null)
        {
            queries.Add(("traces.detail", $"/api/v1/traces/{traceId}"));
        }

        var instrument = await FirstInstrumentAsync($"/api/v1/metrics?{window}");
        if (instrument is not null)
        {
            queries.Add(("metrics.points", $"/api/v1/metrics/points?{window}&{instrument}"));
        }

        var results = new List<QueryResult>();
        foreach (var (name, path) in queries)
        {
            var recorder = new LatencyRecorder();
            var failures = 0;
            string? lastStatus = null;
            for (var i = 0; i < iterations; i++)
            {
                var started = Stopwatch.GetTimestamp();
                using var response = await http.GetAsync(path);
                await response.Content.ReadAsByteArrayAsync();
                var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (response.IsSuccessStatusCode)
                {
                    recorder.Record(elapsed);
                }
                else
                {
                    failures++;
                    lastStatus = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            results.Add(new QueryResult(name, path, recorder.Summarize(), failures, lastStatus));
        }
        return results;
    }

    private async Task<string?> FirstStringAsync(string path, string arrayProperty, string property)
    {
        using var response = await http.GetAsync(path);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        foreach (var item in doc.RootElement.GetProperty(arrayProperty).EnumerateArray())
        {
            return item.GetProperty(property).GetString();
        }
        return null;
    }

    private async Task<string?> FirstInstrumentAsync(string path)
    {
        using var response = await http.GetAsync(path);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.GetProperty("name").GetString() != "http.server.request.duration") continue;
            return string.Join('&',
                $"resourceHash={Uri.EscapeDataString(item.GetProperty("resourceHash").GetString()!)}",
                $"scopeName={Uri.EscapeDataString(item.GetProperty("scopeName").GetString()!)}",
                $"instrumentName={Uri.EscapeDataString(item.GetProperty("name").GetString()!)}",
                $"kind={Uri.EscapeDataString(item.GetProperty("kind").GetString()!)}");
        }
        return null;
    }
}

internal sealed record QueryResult(string Name, string Path, LatencySummary LatencyMs, int Failures, string? LastFailureStatus);
