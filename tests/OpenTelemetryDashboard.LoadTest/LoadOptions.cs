using System.Globalization;

namespace OpenTelemetryDashboard.LoadTest;

internal sealed record LoadOptions
{
    public Uri GrpcEndpoint { get; init; } = new("http://localhost:4317");
    public Uri HttpEndpoint { get; init; } = new("http://localhost:4318");
    public string? ApiKey { get; init; }
    public string? BrowserToken { get; init; }
    public int DurationSeconds { get; init; } = 30;
    public int Workers { get; init; } = 16;
    public int BatchSize { get; init; } = 128;
    public double TargetRequestsPerSecond { get; init; }
    public IReadOnlyList<string> Signals { get; init; } = ["traces", "logs", "metrics"];
    public int Services { get; init; } = 10;
    public int SpreadMinutes { get; init; }
    public int? Pid { get; init; }
    public string Label { get; init; } = "run";
    public string OutputDirectory { get; init; } = "results";
    public int QueryIterations { get; init; } = 30;
    public bool SkipQuery { get; init; }
    public bool SkipIngest { get; init; }
    public bool Overlap { get; init; }

    public static LoadOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i].TrimStart('-');
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++i];
            }
            else
            {
                flags.Add(key);
            }
        }

        var defaults = new LoadOptions();
        return new LoadOptions
        {
            GrpcEndpoint = values.TryGetValue("grpc", out var grpc) ? new Uri(grpc) : defaults.GrpcEndpoint,
            HttpEndpoint = values.TryGetValue("http", out var http) ? new Uri(http) : defaults.HttpEndpoint,
            ApiKey = values.GetValueOrDefault("api-key"),
            BrowserToken = values.GetValueOrDefault("token"),
            DurationSeconds = Int(values, "duration", defaults.DurationSeconds),
            Workers = Int(values, "workers", defaults.Workers),
            BatchSize = Int(values, "batch", defaults.BatchSize),
            TargetRequestsPerSecond = values.TryGetValue("rate", out var rate) ? double.Parse(rate, CultureInfo.InvariantCulture) : 0,
            Signals = values.TryGetValue("signals", out var signals) ? signals.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : defaults.Signals,
            Services = Int(values, "services", defaults.Services),
            SpreadMinutes = Int(values, "spread-minutes", defaults.SpreadMinutes),
            Pid = values.TryGetValue("pid", out var pid) ? int.Parse(pid, CultureInfo.InvariantCulture) : null,
            Label = values.GetValueOrDefault("label") ?? defaults.Label,
            OutputDirectory = values.GetValueOrDefault("out") ?? defaults.OutputDirectory,
            QueryIterations = Int(values, "query-iterations", defaults.QueryIterations),
            SkipQuery = flags.Contains("skip-query"),
            SkipIngest = flags.Contains("skip-ingest"),
            Overlap = flags.Contains("overlap"),
        };
    }

    private static int Int(Dictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw) ? int.Parse(raw, CultureInfo.InvariantCulture) : fallback;
}
