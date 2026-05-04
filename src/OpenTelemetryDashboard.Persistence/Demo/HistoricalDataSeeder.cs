using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;

namespace OpenTelemetryDashboard.Persistence.Demo;

/// <summary>
/// Backfills the dashboard with a week of plausible traces and logs so
/// the SPA's "last 1h / 24h / 7d" filters surface realistic data on a
/// fresh install instead of an empty grid. Writes through the same
/// <see cref="ITraceSink"/> / <see cref="ILogSink"/> the live ingestion
/// pipeline uses, so the data path is identical to a real OTLP push —
/// only the timestamps are backdated.
///
/// Idempotency: skipped when the spans table already contains any rows,
/// so re-enabling the flag on a populated DB is a no-op (rather than a
/// duplicating one). Reset the demo by wiping the storage volume.
/// </summary>
public sealed class HistoricalDataSeeder
{
    // Operation catalog mirroring the kind of traffic `sample-server`
    // produces, so the dashboard's "top operations" widget gets a
    // realistic spread without any infrastructure-specific knowledge.
    // Lognormal duration parameters target medians ~30–100ms with a
    // long tail.
    private static readonly OperationProfile[] Operations =
    [
        new("GET /counter",         40, 3.4, 0.55, 0.01,  "Microsoft.AspNetCore",         SpanKind.Server),
        new("POST /counter/random", 20, 4.2, 0.65, 0.04,  "Microsoft.AspNetCore",         SpanKind.Server),
        new("POST /counter/{value}", 8, 4.0, 0.60, 0.03,  "Microsoft.AspNetCore",         SpanKind.Server),
        new("counter.get",          25, 3.0, 0.50, 0.005, "SampleServer.Counter",         SpanKind.Internal),
        new("counter.mutate",       15, 3.8, 0.70, 0.05,  "SampleServer.Counter",         SpanKind.Internal),
    ];

    private static readonly LogTemplate[] LogTemplates =
    [
        new("Request finished HTTP/1.1 GET /counter 200 in {ms}ms",                    "Microsoft.AspNetCore.Hosting",                  SeverityNumber.Info,  30),
        new("Request finished HTTP/1.1 POST /counter/random 200 in {ms}ms",            "Microsoft.AspNetCore.Hosting",                  SeverityNumber.Info,  18),
        new("Counter mutation accepted (delta={n}, new value={n2})",                   "SampleServer.Counter",                          SeverityNumber.Info,  20),
        new("HybridCache miss for key 'counter:1', falling back to database",          "SampleServer.Cache",                            SeverityNumber.Info,  10),
        new("Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]",                        "Microsoft.EntityFrameworkCore",                 SeverityNumber.Debug,  5),
        new("Slow query detected ({ms}ms > 250ms threshold) — counter table",          "SampleServer.Performance",                      SeverityNumber.Warn,   8),
        new("Redis backpressure: pending={n}, retrying with exponential backoff",      "SampleServer.Cache",                            SeverityNumber.Warn,   3),
        new("Counter value out of expected range (={n}); clamped",                     "SampleServer.Counter",                          SeverityNumber.Warn,   2),
        new("Failed to acquire Redis lock for counter:1 — falling back to DB read",    "SampleServer.Cache",                            SeverityNumber.Error,  2),
        new("Database deadlock detected during counter mutation; transaction aborted", "SampleServer.Counter",                          SeverityNumber.Error,  1),
        new("Health check 'redis' failed: connection timeout after 2000ms",            "Microsoft.Extensions.Diagnostics.HealthChecks", SeverityNumber.Error,  1),
    ];

    private readonly ITraceSink _traceSink;
    private readonly ILogSink _logSink;
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly DemoSeedOptions _options;
    private readonly ILogger<HistoricalDataSeeder> _logger;

    public HistoricalDataSeeder(
        ITraceSink traceSink,
        ILogSink logSink,
        IDbContextFactory<TelemetryDbContext> contextFactory,
        IOptions<DemoSeedOptions> options,
        ILogger<HistoricalDataSeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(traceSink);
        ArgumentNullException.ThrowIfNull(logSink);
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _traceSink = traceSink;
        _logSink = logSink;
        _contextFactory = contextFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        // Idempotency: if anyone (live workload or a previous seeder run)
        // already wrote spans, skip. Avoids piling up duplicate historical
        // data on every dashboard restart.
        await using (var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await ctx.Spans.AnyAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.SkippedExistingSpans();
                return;
            }
        }

        _logger.SeedingStarted(_options.Days, _options.TraceCount, _options.LogCount);

        var rng = new Random(42);

        // Build a Resource representing the sample-server identity, then
        // compute its hash from the canonical attribute set.
        var attributes = new Dictionary<string, object?>
        {
            ["service.namespace"] = "oteldemo",
            ["service.version"] = "1.0.0",
            ["deployment.environment"] = "demo"
        };
        var resourceHash = ResourceHasher.Compute(
            serviceName: "sample-server",
            serviceInstanceId: "server-1",
            schemaUrl: null,
            droppedAttributesCount: 0,
            attributes: attributes);

        var resource = new Resource
        {
            Hash = resourceHash,
            ServiceName = "sample-server",
            ServiceInstanceId = "server-1",
            SchemaUrl = null,
            DroppedAttributesCount = 0,
            Attributes = attributes
        };

        var nowUnixNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
        var windowSeconds = TimeSpan.FromDays(_options.Days).TotalSeconds;

        // Bias toward recent: cube distribution puts ~50% of events in
        // the last day, ~90% in the last 3.
        double sample() => 1.0 - Math.Pow(rng.NextDouble(), 3.0);

        // ---- Traces -----------------------------------------------------
        var totalTraceWeight = Operations.Sum(o => o.Weight);
        var spans = new List<Span>(_options.TraceCount);
        for (var i = 0; i < _options.TraceCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var op = WeightedPick(Operations, o => o.Weight, totalTraceWeight, rng);
            var startNano = (long)(nowUnixNano - windowSeconds * 1_000_000_000L * sample());
            var durationMs = Math.Max(1.0, Math.Exp(op.Mu + Gaussian(rng) * op.Sigma));
            var endNano = startNano + (long)(durationMs * 1_000_000.0);
            var isError = rng.NextDouble() < op.ErrorRate;

            var spanAttributes = new Dictionary<string, object?>
            {
                ["http.route"] = op.Name,
                ["demo.seeder"] = "historical"
            };
            if (isError) spanAttributes["error"] = true;

            spans.Add(new Span
            {
                TraceId = RandomTraceId(rng),
                SpanId = RandomSpanId(rng),
                ResourceHash = resourceHash,
                Name = op.Name,
                Kind = op.Kind,
                StartUnixNano = startNano,
                EndUnixNano = endNano,
                StatusCode = isError ? SpanStatusCode.Error : SpanStatusCode.Ok,
                StatusMessage = isError ? "Simulated failure" : null,
                ScopeName = op.Scope,
                ScopeVersion = "1.0.0",
                Attributes = spanAttributes
            });
        }

        var traceBatch = new TraceBatch([resource], spans);
        await _traceSink.WriteAsync([traceBatch], cancellationToken).ConfigureAwait(false);

        // ---- Logs -------------------------------------------------------
        var totalLogWeight = LogTemplates.Sum(t => t.Weight);
        var logs = new List<LogRecord>(_options.LogCount);
        for (var i = 0; i < _options.LogCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tpl = WeightedPick(LogTemplates, t => t.Weight, totalLogWeight, rng);
            var timeNano = (long)(nowUnixNano - windowSeconds * 1_000_000_000L * sample());
            var body = tpl.Body
                .Replace("{ms}", rng.Next(2, 350).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{n2}", rng.Next(0, 1000).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("{n}",  rng.Next(1, 100).ToString(CultureInfo.InvariantCulture),  StringComparison.Ordinal);

            logs.Add(new LogRecord
            {
                ResourceHash = resourceHash,
                TimeUnixNano = timeNano,
                ObservedTimeUnixNano = timeNano,
                SeverityNumber = tpl.Severity,
                SeverityText = tpl.Severity.ToString(),
                Body = body,
                ScopeName = tpl.Scope,
                ScopeVersion = "1.0.0",
                Attributes = new Dictionary<string, object?>
                {
                    ["demo.seeder"] = "historical"
                }
            });
        }

        var logBatch = new LogBatch([resource], logs);
        await _logSink.WriteAsync([logBatch], cancellationToken).ConfigureAwait(false);

        _logger.SeedingCompleted(spans.Count, logs.Count, _options.Days);
    }

    private static TraceId RandomTraceId(Random rng)
    {
        Span<byte> bytes = stackalloc byte[TraceId.SizeInBytes];
        rng.NextBytes(bytes);
        // Avoid the all-zeros sentinel — the domain rejects it.
        if (bytes.IndexOfAnyExcept((byte)0) < 0) bytes[0] = 1;
        return TraceId.FromBytes(bytes);
    }

    private static SpanId RandomSpanId(Random rng)
    {
        Span<byte> bytes = stackalloc byte[SpanId.SizeInBytes];
        rng.NextBytes(bytes);
        if (bytes.IndexOfAnyExcept((byte)0) < 0) bytes[0] = 1;
        return SpanId.FromBytes(bytes);
    }

    private static T WeightedPick<T>(T[] items, Func<T, double> weight, double total, Random rng)
    {
        var roll = rng.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll <= 0) return item;
        }
        return items[^1];
    }

    private static double Gaussian(Random rng)
    {
        // Box–Muller — mean 0, std-dev 1.
        var u = Math.Max(rng.NextDouble(), 1e-9);
        var v = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(2.0 * Math.PI * v);
    }

    private sealed record OperationProfile(
        string Name,
        double Weight,
        double Mu,
        double Sigma,
        double ErrorRate,
        string Scope,
        SpanKind Kind);

    private sealed record LogTemplate(
        string Body,
        string Scope,
        SeverityNumber Severity,
        double Weight);
}

/// <summary>
/// Logging surface for the demo seeder. Public so the host's startup
/// code can call <see cref="DemoSeedingFailed"/> after catching a seeder
/// exception, keeping the boot path's try/catch warning analyzer-clean
/// (CA1848 mandates LoggerMessage source-gen for hot-path logging).
/// </summary>
public static partial class HistoricalDataSeederLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Demo seeder skipped — telemetry storage already contains spans.")]
    public static partial void SkippedExistingSpans(this ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Seeding {Days}d of demo telemetry: {TraceCount} traces, {LogCount} logs.")]
    public static partial void SeedingStarted(this ILogger logger, int days, int traceCount, int logCount);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Demo seed complete — {SpanCount} spans, {LogCount} logs over the last {Days} days.")]
    public static partial void SeedingCompleted(this ILogger logger, int spanCount, int logCount, int days);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Demo historical-data seeding failed (non-fatal).")]
    public static partial void DemoSeedingFailed(this ILogger logger, Exception exception);
}
