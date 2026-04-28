using System.Buffers;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// Opt-in dogfooding: when enabled, the dashboard process exports its own
/// logs / traces / metrics back to its own OTLP HTTP endpoint. Off in
/// production by default; on in <c>appsettings.Development.json</c> so a
/// `dotnet run` (or `docker compose up`) instantly populates the Logs /
/// Traces / Metrics pages with real data.
/// </summary>
public sealed class SelfInstrumentationOptions
{
    public const string SectionName = "Dashboard:SelfInstrumentation";

    /// <summary>
    /// Master switch. False in production by default.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// OTLP/HTTP endpoint to push to. Defaults to the local listener so the
    /// dashboard talks to itself; override to forward to another collector.
    /// </summary>
    public string Endpoint { get; init; } = "http://localhost:4318";

    /// <summary>
    /// Logical service name reported via the <c>service.name</c> resource
    /// attribute. Drives the "Application" filter on the UI.
    /// </summary>
    public string ServiceName { get; init; } = "OpenTelemetryDashboard";

    /// <summary>
    /// Override for the <c>x-otlp-api-key</c> header sent to the OTLP endpoint.
    /// Leave null/empty to fall back to <c>Dashboard:Otlp:ApiKey</c> — that's
    /// the right value when the dashboard is exporting to itself, and avoids
    /// duplicating the same secret in two config keys. Set only when forwarding
    /// to an external collector that expects a different token.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Activity names that should never be recorded. Used to silence noisy
    /// host-lifetime activities such as the AspNetCore "main" span (which
    /// stays open for the whole process lifetime and dominates the trace
    /// listing). Match is exact, case-sensitive on
    /// <c>Activity.OperationName</c> / <c>SamplingParameters.Name</c>.
    /// </summary>
    public string[] IgnoredActivityNames { get; init; } = ["main"];
}

public static class SelfInstrumentationOptionsExtensions
{
    // Self-instrumentation: when enabled, the dashboard exports its own logs /
    // traces / metrics to its own OTLP HTTP receiver. Off by default; on in
    // Development so the UI shows live data out of the box.
    public static WebApplicationBuilder AddSelfInstrumentation(this WebApplicationBuilder builder)
    {
        var selfInstrumentation = builder.Configuration
            .GetSection(SelfInstrumentationOptions.SectionName)
            .Get<SelfInstrumentationOptions>() ?? new SelfInstrumentationOptions();

        if (!selfInstrumentation.Enabled) return builder;

        // When the dashboard exports to itself the OTLP API key is the same
        // value the ingest pipeline accepts, so default to it instead of
        // forcing the operator to set the secret in two places.
        var apiKey = !string.IsNullOrWhiteSpace(selfInstrumentation.ApiKey)
            ? selfInstrumentation.ApiKey
            : builder.Configuration.GetSection(DashboardAuthOptions.SectionName)
                .Get<DashboardAuthOptions>()?.Otlp.ApiKey;

        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName: selfInstrumentation.ServiceName, autoGenerateServiceInstanceId: true)
            .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]);

        // SDK >= 1.10 stops auto-appending /v1/{signal} when Endpoint is set
        // programmatically, so we point each exporter at its full URL.
        var baseEndpoint = selfInstrumentation.Endpoint.TrimEnd('/');
        Action<OtlpExporterOptions> ConfigureOtlp(string signalPath) => o =>
        {
            o.Endpoint = new Uri($"{baseEndpoint}/{signalPath}");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                o.Headers = $"x-otlp-api-key={apiKey}";
            }
        };

        var ignoredActivities = new HashSet<string>(
            selfInstrumentation.IgnoredActivityNames ?? [],
            StringComparer.Ordinal);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName: selfInstrumentation.ServiceName, autoGenerateServiceInstanceId: true)
                .AddAttributes([new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(t => t
                // Drop activities whose OperationName is on the ignore list
                // before any processor or exporter sees them. ParentBased keeps
                // remote children sampled like their parent (default Otel).
                .SetSampler(new ParentBasedSampler(new IgnoredNamesSampler(ignoredActivities)))
                .AddAspNetCoreInstrumentation(o =>
                {
                    // Liveness probe is pure noise; everything else (SPA reads
                    // on /api/v1/* and the self-push on /v1/{signal}) is kept
                    // so the user can correlate frontend reads with their EF
                    // SELECT spans and observe the OTLP ingest of self pushes
                    // with their EF INSERT spans.
                    o.Filter = ctx => ctx.Request.Path != "/healthz";
                })
                .AddHttpClientInstrumentation(o =>
                {
                    // Skip the outbound side of OTLP exports: the *server* span
                    // for the same POST is already captured above, and tracing
                    // the client side feeds an unbounded loop (every export
                    // span lands in the next batch, re-exported, traced again).
                    o.FilterHttpRequestMessage = req => !IsOtlpExportRequest(req?.RequestUri);
                })
                // EF Core spans carry the rendered SQL on `db.query.text`. The
                // instrumentation uses `db.name` as the activity DisplayName by
                // default — for SQLite that's the literal string "main" (the
                // built-in default database), which floods the trace feed with
                // anonymous "main" entries. Replace it with the SQL verb plus
                // the target table so the listing is readable.
                .AddEntityFrameworkCoreInstrumentation(o =>
                {
                    o.EnrichWithIDbCommand = (activity, command) =>
                    {
                        var name = BuildDbActivityName(command.CommandText);
                        if (name is not null) activity.DisplayName = name;
                    };
                })
                .AddSource("OpenTelemetryDashboard.*")
                .AddOtlpExporter(ConfigureOtlp("v1/traces")))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddMeter("OpenTelemetryDashboard.*")
                .AddOtlpExporter(ConfigureOtlp("v1/metrics")));

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.SetResourceBuilder(resource);
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
            o.AddOtlpExporter(ConfigureOtlp("v1/logs"));
        });

        // Avoid feedback loops: silence the OpenTelemetry SDK's own diagnostic
        // logs at the OTel sink (they would be re-exported, then logged again).
        builder.Logging.AddFilter("OpenTelemetry", LogLevel.Warning);

        return builder;
    }

    private static readonly SearchValues<char> IdentifierStopChars =
        SearchValues.Create([' ', '(', ',', '\n', '\r', '\t']);

    /// <summary>
    /// Builds a readable activity name from the rendered SQL: the verb (SELECT,
    /// INSERT, UPDATE, DELETE) followed by the first table reference when one
    /// can be extracted. Returns <c>null</c> if the input is empty so the
    /// caller can leave the existing DisplayName untouched.
    /// </summary>
    private static string? BuildDbActivityName(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;

        var span = sql.AsSpan().TrimStart();
        var firstSpace = span.IndexOf(' ');
        if (firstSpace <= 0) return span.ToString();

        var verb = span[..firstSpace].ToString().ToUpperInvariant();
        var rest = span[(firstSpace + 1)..];

        var keyword = verb switch
        {
            "SELECT" or "DELETE" => "FROM ",
            "INSERT" => "INTO ",
            "UPDATE" => null, // table comes right after UPDATE
            _ => null
        };

        var tableSegment = verb == "UPDATE" ? rest : SkipTo(rest, keyword);
        var table = ExtractIdentifier(tableSegment);
        return table is null ? verb : $"{verb} {table}";
    }

    private static ReadOnlySpan<char> SkipTo(ReadOnlySpan<char> input, string? keyword)
    {
        if (keyword is null) return ReadOnlySpan<char>.Empty;
        var idx = input.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? ReadOnlySpan<char>.Empty : input[(idx + keyword.Length)..];
    }

    private static string? ExtractIdentifier(ReadOnlySpan<char> input)
    {
        input = input.TrimStart();
        if (input.IsEmpty) return null;

        // Strip a single layer of quoting (", `, [, ]) used by SQLite/SqlServer/Postgres.
        if (input[0] is '"' or '`' or '[')
        {
            var closer = input[0] switch { '[' => ']', _ => input[0] };
            var end = input[1..].IndexOf(closer);
            return end > 0 ? input.Slice(1, end).ToString() : null;
        }

        var stop = input.IndexOfAny(IdentifierStopChars);
        var slice = stop > 0 ? input[..stop] : input;
        return slice.Length > 0 ? slice.ToString() : null;
    }

    private static bool IsOtlpExportRequest(Uri? uri)
    {
        if (uri is null) return false;
        var path = uri.AbsolutePath;
        return path.StartsWith("/v1/traces", StringComparison.Ordinal)
            || path.StartsWith("/v1/logs", StringComparison.Ordinal)
            || path.StartsWith("/v1/metrics", StringComparison.Ordinal);
    }

    /// <summary>
    /// Sampler that drops activities whose creation-time name (the
    /// <c>SamplingParameters.Name</c>, which mirrors <c>Activity.OperationName</c>)
    /// is on the ignore list, and otherwise records and samples everything.
    /// </summary>
    private sealed class IgnoredNamesSampler : Sampler
    {
        private static readonly SamplingResult Drop = new(SamplingDecision.Drop);
        private static readonly SamplingResult Sample = new(SamplingDecision.RecordAndSample);

        private readonly HashSet<string> _ignored;

        public IgnoredNamesSampler(HashSet<string> ignored)
        {
            _ignored = ignored;
            Description = $"IgnoredNamesSampler({string.Join(",", ignored)})";
        }

        public override SamplingResult ShouldSample(in SamplingParameters parameters)
            => _ignored.Contains(parameters.Name) ? Drop : Sample;
    }
}