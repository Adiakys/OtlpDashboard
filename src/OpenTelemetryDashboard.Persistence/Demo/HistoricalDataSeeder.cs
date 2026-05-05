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
/// <para>
/// Each trace is built from a <see cref="Scenario"/> — a small story
/// (cache hit, DB fallback, mutation deadlock, healthcheck failure)
/// that nails down the root span, its children, and the logs each
/// success/error variant emits. Logs carry the trace's <see cref="TraceId"/>
/// and the <see cref="SpanId"/> of the span they fired inside, so the
/// trace-detail view shows alert markers exactly where the user expects
/// them. A background-noise pass adds standalone logs (no trace
/// correlation) at the end so the logs page isn't 100% trace-correlated
/// — real systems aren't either.
/// </para>
///
/// Idempotency: skipped when the spans table already contains any rows,
/// so re-enabling the flag on a populated DB is a no-op (rather than a
/// duplicating one). Reset the demo by wiping the storage volume.
/// </summary>
public sealed class HistoricalDataSeeder
{
    /// <summary>
    /// End-to-end "story" for a trace. Holds the root span profile, the
    /// sequence of child spans, and the log scripts for the success and
    /// error variants. Picking one scenario fully determines the shape
    /// of the resulting trace and its correlated logs.
    /// </summary>
    private sealed record Scenario(
        string Name,
        string RootName,
        string RootScope,
        SpanKind RootKind,
        // Lognormal duration parameters for the root span (median ~ exp(Mu) ms).
        double Mu,
        double Sigma,
        // Selection weight for WeightedPick.
        double Weight,
        // Probability that this trace fails. The error variant flips
        // <see cref="ErrorSpanIndex"/> (and the root) to Error and emits
        // the <see cref="ErrorLogs"/> instead of the success ones.
        double ErrorRate,
        SpanLayout[] Children,
        LogScript[] SuccessLogs,
        LogScript[] ErrorLogs,
        // Index into Children of the span that "caused" the error.
        // The root is always also marked Error (matches OTel convention).
        int ErrorSpanIndex);

    /// <summary>
    /// One child span of a trace. Timing is expressed as fractions of the
    /// root duration so all children stay neatly nested inside the root
    /// regardless of how long the trace ends up running. <see cref="ParentIndex"/>
    /// lets a child attach to *another* child instead of the root, which
    /// is how real traces build a deep tree (middleware → handler →
    /// repository → db). 0 means "child of root"; 1+ refers to a previous
    /// child in the scenario's <c>Children</c> array.
    /// </summary>
    private sealed record SpanLayout(
        string Name,
        string Scope,
        SpanKind Kind,
        // Where, in [0,1] of the root duration, this child starts.
        double StartFraction,
        // How long, in [0,1] of the root duration, this child lasts.
        double DurationFraction,
        int ParentIndex = 0);

    /// <summary>
    /// One log line tied to a specific span of the trace. The body may
    /// contain `{ms}`, `{n}`, `{n2}` placeholders which the seeder fills
    /// with random values so the demo doesn't show identical strings on
    /// every record.
    /// </summary>
    private sealed record LogScript(
        string Body,
        string Scope,
        SeverityNumber Severity,
        // 0 = root, 1+ = children[i-1].
        int AttachSpanIndex,
        // [0,1] within the target span's duration.
        double TimeFraction);

    // Background logs: fire independently of any trace. Roughly modelled
    // after a hosting framework's lifecycle / GC / healthcheck noise.
    private sealed record BackgroundLog(
        string Body,
        string Scope,
        SeverityNumber Severity,
        double Weight);

    private static readonly Scenario[] Scenarios =
    [
        // GET /counter — cache hit, the happy path that dominates a healthy stack.
        new Scenario(
            Name: "get_counter_cache_hit",
            RootName: "GET /counter",
            RootScope: "Microsoft.AspNetCore",
            RootKind: SpanKind.Server,
            Mu: 3.0, Sigma: 0.45, Weight: 38, ErrorRate: 0.005,
            Children:
            [
                new SpanLayout("redis.get counter:1",     "StackExchange.Redis",        SpanKind.Client,   0.05, 0.20),
                new SpanLayout("counter.serialize",       "SampleServer.Counter",       SpanKind.Internal, 0.55, 0.30),
            ],
            SuccessLogs:
            [
                new LogScript("HybridCache hit for key 'counter:1'",                                  "SampleServer.Cache",             SeverityNumber.Info,  1, 0.5),
                new LogScript("Request finished HTTP/1.1 GET /counter 200 in {ms}ms",                 "Microsoft.AspNetCore.Hosting",   SeverityNumber.Info,  0, 0.95),
            ],
            ErrorLogs:
            [
                new LogScript("Redis backpressure: pending={n}, retrying with exponential backoff",   "SampleServer.Cache",             SeverityNumber.Warn,  1, 0.4),
                new LogScript("Failed to acquire Redis lock for counter:1 — request aborted",         "SampleServer.Cache",             SeverityNumber.Error, 1, 0.7),
                new LogScript("Request finished HTTP/1.1 GET /counter 503 in {ms}ms",                 "Microsoft.AspNetCore.Hosting",   SeverityNumber.Error, 0, 0.95),
            ],
            ErrorSpanIndex: 1),

        // GET /counter — cache miss, falls back to DB. Slower median; an
        // error here means even the DB read failed (rare).
        new Scenario(
            Name: "get_counter_cache_miss",
            RootName: "GET /counter",
            RootScope: "Microsoft.AspNetCore",
            RootKind: SpanKind.Server,
            Mu: 3.9, Sigma: 0.55, Weight: 12, ErrorRate: 0.02,
            Children:
            [
                new SpanLayout("redis.get counter:1",       "StackExchange.Redis",                 SpanKind.Client,   0.05, 0.10),
                new SpanLayout("pg.query SELECT counter",   "Npgsql",                              SpanKind.Client,   0.20, 0.55),
                new SpanLayout("redis.set counter:1",       "StackExchange.Redis",                 SpanKind.Client,   0.80, 0.10),
            ],
            SuccessLogs:
            [
                new LogScript("HybridCache miss for key 'counter:1', falling back to database",      "SampleServer.Cache",             SeverityNumber.Info,  1, 0.5),
                new LogScript("Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]",                    "Microsoft.EntityFrameworkCore",  SeverityNumber.Debug, 2, 0.6),
                new LogScript("Request finished HTTP/1.1 GET /counter 200 in {ms}ms",                "Microsoft.AspNetCore.Hosting",   SeverityNumber.Info,  0, 0.95),
            ],
            ErrorLogs:
            [
                new LogScript("HybridCache miss for key 'counter:1', falling back to database",      "SampleServer.Cache",             SeverityNumber.Info,  1, 0.4),
                new LogScript("Slow query detected ({ms}ms > 250ms threshold) — counter table",      "SampleServer.Performance",       SeverityNumber.Warn,  2, 0.6),
                new LogScript("Database read failed: connection reset by peer",                      "Microsoft.EntityFrameworkCore",  SeverityNumber.Error, 2, 0.7),
                new LogScript("Request finished HTTP/1.1 GET /counter 500 in {ms}ms",                "Microsoft.AspNetCore.Hosting",   SeverityNumber.Error, 0, 0.95),
            ],
            ErrorSpanIndex: 2),

        // POST /counter/{value} — write path with optional deadlock under
        // contention. The error story is a classic deadlock-victim retry
        // that ultimately gives up.
        new Scenario(
            Name: "post_counter_value",
            RootName: "POST /counter/{value}",
            RootScope: "Microsoft.AspNetCore",
            RootKind: SpanKind.Server,
            Mu: 4.0, Sigma: 0.50, Weight: 14, ErrorRate: 0.04,
            Children:
            [
                new SpanLayout("counter.mutate",            "SampleServer.Counter",                SpanKind.Internal, 0.05, 0.30),
                new SpanLayout("pg.query UPDATE counter",   "Npgsql",                              SpanKind.Client,   0.40, 0.50),
            ],
            SuccessLogs:
            [
                new LogScript("Counter mutation accepted (delta={n}, new value={n2})",               "SampleServer.Counter",           SeverityNumber.Info,  1, 0.5),
                new LogScript("Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]",                    "Microsoft.EntityFrameworkCore",  SeverityNumber.Debug, 2, 0.7),
                new LogScript("Request finished HTTP/1.1 POST /counter/{value} 204 in {ms}ms",       "Microsoft.AspNetCore.Hosting",   SeverityNumber.Info,  0, 0.95),
            ],
            ErrorLogs:
            [
                new LogScript("Counter mutation accepted (delta={n}, new value={n2})",               "SampleServer.Counter",           SeverityNumber.Info,  1, 0.4),
                new LogScript("Database deadlock detected during counter mutation; transaction aborted", "Microsoft.EntityFrameworkCore", SeverityNumber.Error, 2, 0.7),
                new LogScript("Counter mutation failed: deadlock victim, retries exhausted",         "SampleServer.Counter",           SeverityNumber.Error, 1, 0.85),
                new LogScript("Request finished HTTP/1.1 POST /counter/{value} 500 in {ms}ms",       "Microsoft.AspNetCore.Hosting",   SeverityNumber.Error, 0, 0.95),
            ],
            ErrorSpanIndex: 2),

        // POST /counter/random — write path with input validation that
        // can fail when the random value lands out of bounds. Failure
        // never touches the DB (validation aborts earlier).
        new Scenario(
            Name: "post_counter_random",
            RootName: "POST /counter/random",
            RootScope: "Microsoft.AspNetCore",
            RootKind: SpanKind.Server,
            Mu: 4.1, Sigma: 0.55, Weight: 22, ErrorRate: 0.05,
            Children:
            [
                new SpanLayout("counter.random",            "SampleServer.Counter",                SpanKind.Internal, 0.05, 0.20),
                new SpanLayout("counter.mutate",            "SampleServer.Counter",                SpanKind.Internal, 0.30, 0.25),
                new SpanLayout("pg.query UPDATE counter",   "Npgsql",                              SpanKind.Client,   0.55, 0.35),
            ],
            SuccessLogs:
            [
                new LogScript("Counter randomized to {n}",                                           "SampleServer.Counter",           SeverityNumber.Info,  1, 0.5),
                new LogScript("Counter mutation accepted (delta={n}, new value={n2})",               "SampleServer.Counter",           SeverityNumber.Info,  2, 0.5),
                new LogScript("Request finished HTTP/1.1 POST /counter/random 200 in {ms}ms",        "Microsoft.AspNetCore.Hosting",   SeverityNumber.Info,  0, 0.95),
            ],
            ErrorLogs:
            [
                new LogScript("Counter value out of expected range (={n}); clamped",                 "SampleServer.Counter",           SeverityNumber.Warn,  1, 0.6),
                new LogScript("Validation failed for counter mutation: value {n} exceeds max",       "SampleServer.Counter",           SeverityNumber.Error, 1, 0.85),
                new LogScript("Request finished HTTP/1.1 POST /counter/random 400 in {ms}ms",        "Microsoft.AspNetCore.Hosting",   SeverityNumber.Warn, 0, 0.95),
            ],
            ErrorSpanIndex: 1),

        // POST /counter/batch — full middleware-pipeline trace with a
        // deeper tree (server → handler → repo → db, plus a side branch
        // for cache invalidation). Sized about ~1 in 10 traces so the
        // user lands on at least a handful of "complex" traces in the
        // demo set, useful to exercise the trace-detail rendering.
        // ParentIndex wires:
        //   0 root
        //   ├── 1 middleware.auth          (root)
        //   ├── 2 middleware.routing       (root)
        //   └── 3 handler.batch_post       (root)
        //         ├── 4 validator.batch    (handler=3)
        //         ├── 5 repository.batch   (handler=3)
        //         │     ├── 6 pg.BEGIN     (repository=5)
        //         │     ├── 7 pg.UPDATE    (repository=5)
        //         │     └── 8 pg.COMMIT    (repository=5)
        //         └── 9 cache.invalidate   (handler=3)
        new Scenario(
            Name: "post_counter_batch",
            RootName: "POST /counter/batch",
            RootScope: "Microsoft.AspNetCore",
            RootKind: SpanKind.Server,
            Mu: 4.7, Sigma: 0.55, Weight: 6, ErrorRate: 0.06,
            Children:
            [
                new SpanLayout("middleware.authentication", "OpenTelemetryDashboard.Auth",         SpanKind.Internal, 0.02, 0.04, ParentIndex: 0),
                new SpanLayout("middleware.routing",        "Microsoft.AspNetCore.Routing",        SpanKind.Internal, 0.06, 0.03, ParentIndex: 0),
                new SpanLayout("handler.batch_post",        "SampleServer.Handlers",               SpanKind.Internal, 0.10, 0.85, ParentIndex: 0),
                new SpanLayout("validator.batch",           "SampleServer.Validation",             SpanKind.Internal, 0.13, 0.08, ParentIndex: 3),
                new SpanLayout("repository.save_batch",     "SampleServer.Repositories",           SpanKind.Internal, 0.25, 0.55, ParentIndex: 3),
                new SpanLayout("pg.BEGIN TRANSACTION",      "Npgsql",                              SpanKind.Client,   0.27, 0.04, ParentIndex: 5),
                new SpanLayout("pg.UPDATE counter (batch)", "Npgsql",                              SpanKind.Client,   0.33, 0.40, ParentIndex: 5),
                new SpanLayout("pg.COMMIT",                 "Npgsql",                              SpanKind.Client,   0.75, 0.04, ParentIndex: 5),
                new SpanLayout("cache.invalidate counter",  "StackExchange.Redis",                 SpanKind.Client,   0.85, 0.08, ParentIndex: 3),
            ],
            SuccessLogs:
            [
                new LogScript("Authenticated request via Bearer token (sub={n})",                    "OpenTelemetryDashboard.Auth",    SeverityNumber.Info,  1, 0.5),
                new LogScript("Validated batch payload ({n} entries)",                               "SampleServer.Validation",        SeverityNumber.Info,  4, 0.5),
                new LogScript("Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]",                    "Microsoft.EntityFrameworkCore",  SeverityNumber.Debug, 7, 0.6),
                new LogScript("Committed batch transaction ({n} rows)",                              "SampleServer.Repositories",      SeverityNumber.Info,  8, 0.7),
                new LogScript("Cache invalidated for {n} keys",                                      "SampleServer.Cache",             SeverityNumber.Info,  9, 0.5),
                new LogScript("Request finished HTTP/1.1 POST /counter/batch 200 in {ms}ms",         "Microsoft.AspNetCore.Hosting",   SeverityNumber.Info,  0, 0.95),
            ],
            ErrorLogs:
            [
                new LogScript("Authenticated request via Bearer token (sub={n})",                    "OpenTelemetryDashboard.Auth",    SeverityNumber.Info,  1, 0.5),
                new LogScript("Validated batch payload ({n} entries)",                               "SampleServer.Validation",        SeverityNumber.Info,  4, 0.5),
                new LogScript("Slow query detected ({ms}ms > 250ms threshold) — counter table",     "SampleServer.Performance",       SeverityNumber.Warn,  7, 0.6),
                new LogScript("Database deadlock detected during batch update; transaction rolled back", "Microsoft.EntityFrameworkCore", SeverityNumber.Error, 7, 0.85),
                new LogScript("Repository.save_batch failed; surfacing 500 to caller",               "SampleServer.Repositories",      SeverityNumber.Error, 5, 0.95),
                new LogScript("Request finished HTTP/1.1 POST /counter/batch 500 in {ms}ms",         "Microsoft.AspNetCore.Hosting",   SeverityNumber.Error, 0, 0.95),
            ],
            ErrorSpanIndex: 7),

        // Periodic healthcheck — separate root span, single child for
        // the actual probe. Errors here are the "redis is down" story
        // and produce a single, focused error log.
        new Scenario(
            Name: "healthcheck_redis",
            RootName: "GET /healthz",
            RootScope: "Microsoft.AspNetCore",
            RootKind: SpanKind.Server,
            Mu: 2.5, Sigma: 0.40, Weight: 8, ErrorRate: 0.06,
            Children:
            [
                new SpanLayout("redis.ping",                "StackExchange.Redis",                 SpanKind.Client,   0.10, 0.80),
            ],
            SuccessLogs:
            [
                new LogScript("Health check 'redis' completed in {ms}ms",                            "Microsoft.Extensions.Diagnostics.HealthChecks", SeverityNumber.Info, 1, 0.7),
            ],
            ErrorLogs:
            [
                new LogScript("Health check 'redis' failed: connection timeout after 2000ms",        "Microsoft.Extensions.Diagnostics.HealthChecks", SeverityNumber.Error, 1, 0.8),
                new LogScript("Reporting unhealthy status to /healthz consumers",                    "Microsoft.Extensions.Diagnostics.HealthChecks", SeverityNumber.Warn, 0, 0.95),
            ],
            ErrorSpanIndex: 1),
    ];

    // Lifecycle / GC / scheduled-job logs that run independently of any
    // request — emitted at a fixed proportion of the total log volume so
    // the logs page isn't 100% trace-correlated.
    private static readonly BackgroundLog[] BackgroundLogs =
    [
        new("Hosted service 'TelemetryRetentionWorker' running sweep cycle", "OpenTelemetryDashboard.Retention",        SeverityNumber.Info,  10),
        new("GC freeing {n}MB heap (gen2)",                                  "Runtime",                                  SeverityNumber.Debug, 12),
        new("Application started. Listening on http://[::]:8080",            "Microsoft.Hosting.Lifetime",               SeverityNumber.Info,   2),
        new("Configuration reloaded: {n} keys changed",                      "Microsoft.Extensions.Configuration",       SeverityNumber.Info,   3),
        new("Background queue depth = {n}",                                  "SampleServer.Queue",                       SeverityNumber.Debug,  8),
        new("Slow GC pause detected: {ms}ms (gen2)",                         "Runtime",                                  SeverityNumber.Warn,   2),
        new("Connection pool exhaustion approaching: {n}/100 in use",        "Npgsql",                                   SeverityNumber.Warn,   1),
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
        double sampleAge() => 1.0 - Math.Pow(rng.NextDouble(), 3.0);

        // ---- Traces + correlated logs ----------------------------------
        // Built together so each log can carry the TraceId/SpanId of the
        // span it fired inside. We collect everything into two flat lists
        // and ship in one batch each at the end.
        var totalTraceWeight = Scenarios.Sum(s => s.Weight);
        var spans = new List<Span>(_options.TraceCount * 3);
        var correlatedLogs = new List<LogRecord>(_options.TraceCount * 3);

        for (var i = 0; i < _options.TraceCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scenario = WeightedPick(Scenarios, s => s.Weight, totalTraceWeight, rng);
            var rootStartNano = (long)(nowUnixNano - windowSeconds * 1_000_000_000L * sampleAge());
            var rootDurationMs = Math.Max(1.0, Math.Exp(scenario.Mu + Gaussian(rng) * scenario.Sigma));
            var rootDurationNano = (long)(rootDurationMs * 1_000_000.0);
            var rootEndNano = rootStartNano + rootDurationNano;
            var isError = rng.NextDouble() < scenario.ErrorRate;

            var traceId = RandomTraceId(rng);
            var rootSpanId = RandomSpanId(rng);

            // Track each span's (id, start, duration) for log placement.
            var spanIds = new SpanId[1 + scenario.Children.Length];
            var spanStarts = new long[1 + scenario.Children.Length];
            var spanDurations = new long[1 + scenario.Children.Length];
            spanIds[0] = rootSpanId;
            spanStarts[0] = rootStartNano;
            spanDurations[0] = rootDurationNano;

            // Root span.
            spans.Add(new Span
            {
                TraceId = traceId,
                SpanId = rootSpanId,
                ParentSpanId = null,
                ResourceHash = resourceHash,
                Name = scenario.RootName,
                Kind = scenario.RootKind,
                StartUnixNano = rootStartNano,
                EndUnixNano = rootEndNano,
                StatusCode = isError ? SpanStatusCode.Error : SpanStatusCode.Ok,
                StatusMessage = isError ? StatusMessageForScenario(scenario) : null,
                ScopeName = scenario.RootScope,
                ScopeVersion = "1.0.0",
                Attributes = ScenarioAttributes(scenario, isError, isRoot: true)
            });

            // Children.
            for (var c = 0; c < scenario.Children.Length; c++)
            {
                var child = scenario.Children[c];
                var childStart = rootStartNano + (long)(rootDurationNano * child.StartFraction);
                var childDuration = (long)(rootDurationNano * child.DurationFraction);
                var childEnd = childStart + childDuration;
                var childIsError = isError && c == scenario.ErrorSpanIndex;
                var childSpanId = RandomSpanId(rng);

                spanIds[c + 1] = childSpanId;
                spanStarts[c + 1] = childStart;
                spanDurations[c + 1] = childDuration;

                // Parent resolution: ParentIndex=0 → root; 1+ → an
                // earlier child. Bounds-clamp to root if a scenario
                // accidentally points past the current cursor.
                var parentSpanId = child.ParentIndex > 0 && child.ParentIndex - 1 < c
                    ? spanIds[child.ParentIndex]
                    : rootSpanId;

                spans.Add(new Span
                {
                    TraceId = traceId,
                    SpanId = childSpanId,
                    ParentSpanId = parentSpanId,
                    ResourceHash = resourceHash,
                    Name = child.Name,
                    Kind = child.Kind,
                    StartUnixNano = childStart,
                    EndUnixNano = childEnd,
                    StatusCode = childIsError ? SpanStatusCode.Error : SpanStatusCode.Ok,
                    StatusMessage = childIsError ? StatusMessageForScenario(scenario) : null,
                    ScopeName = child.Scope,
                    ScopeVersion = "1.0.0",
                    Attributes = new Dictionary<string, object?>
                    {
                        ["demo.seeder"] = "historical",
                        ["demo.scenario"] = scenario.Name
                    }
                });
            }

            // Logs for this trace.
            var script = isError ? scenario.ErrorLogs : scenario.SuccessLogs;
            foreach (var line in script)
            {
                var idx = line.AttachSpanIndex;
                if (idx < 0 || idx >= spanIds.Length) continue; // safety
                var time = spanStarts[idx] + (long)(spanDurations[idx] * line.TimeFraction);
                var body = FillTemplate(line.Body, rng);
                correlatedLogs.Add(new LogRecord
                {
                    ResourceHash = resourceHash,
                    TimeUnixNano = time,
                    ObservedTimeUnixNano = time,
                    SeverityNumber = line.Severity,
                    SeverityText = line.Severity.ToString(),
                    Body = body,
                    TraceId = traceId,
                    SpanId = spanIds[idx],
                    ScopeName = line.Scope,
                    ScopeVersion = "1.0.0",
                    Attributes = new Dictionary<string, object?>
                    {
                        ["demo.seeder"] = "historical",
                        ["demo.scenario"] = scenario.Name
                    }
                });
            }
        }

        // ---- Background logs (uncorrelated) -----------------------------
        // Padded so the total log count roughly matches the configured
        // target. Adjusted floor so a tiny configured value still gets
        // some background noise.
        var background = Math.Max(0, _options.LogCount - correlatedLogs.Count);
        var totalBgWeight = BackgroundLogs.Sum(b => b.Weight);
        var bgLogs = new List<LogRecord>(background);
        for (var i = 0; i < background; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tpl = WeightedPick(BackgroundLogs, b => b.Weight, totalBgWeight, rng);
            var time = (long)(nowUnixNano - windowSeconds * 1_000_000_000L * sampleAge());
            bgLogs.Add(new LogRecord
            {
                ResourceHash = resourceHash,
                TimeUnixNano = time,
                ObservedTimeUnixNano = time,
                SeverityNumber = tpl.Severity,
                SeverityText = tpl.Severity.ToString(),
                Body = FillTemplate(tpl.Body, rng),
                ScopeName = tpl.Scope,
                ScopeVersion = "1.0.0",
                Attributes = new Dictionary<string, object?>
                {
                    ["demo.seeder"] = "historical",
                    ["demo.scenario"] = "background"
                }
            });
        }

        var traceBatch = new TraceBatch([resource], spans);
        await _traceSink.WriteAsync([traceBatch], cancellationToken).ConfigureAwait(false);

        var allLogs = new List<LogRecord>(correlatedLogs.Count + bgLogs.Count);
        allLogs.AddRange(correlatedLogs);
        allLogs.AddRange(bgLogs);
        var logBatch = new LogBatch([resource], allLogs);
        await _logSink.WriteAsync([logBatch], cancellationToken).ConfigureAwait(false);

        _logger.SeedingCompleted(spans.Count, allLogs.Count, _options.Days);
    }

    private static string StatusMessageForScenario(Scenario s) => s.Name switch
    {
        "get_counter_cache_hit"   => "Redis lock acquisition failed",
        "get_counter_cache_miss"  => "Database read failed after cache miss",
        "post_counter_value"      => "Database deadlock during counter mutation",
        "post_counter_random"     => "Counter validation failed",
        "healthcheck_redis"       => "Redis probe timed out",
        _                         => "Simulated failure",
    };

    private static Dictionary<string, object?> ScenarioAttributes(Scenario s, bool isError, bool isRoot)
    {
        var attrs = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["demo.seeder"] = "historical",
            ["demo.scenario"] = s.Name,
        };
        if (isRoot)
        {
            // Pseudo HTTP attributes — nothing reads them in the dashboard
            // today, but they surface in the span-detail panel and make
            // the seeded data look like real ASP.NET Core traces.
            attrs["http.route"] = s.RootName;
            attrs["http.request.method"] = s.RootName.Split(' ')[0];
        }
        if (isError) attrs["error"] = true;
        return attrs;
    }

    private static string FillTemplate(string template, Random rng)
    {
        return template
            .Replace("{ms}", rng.Next(2, 350).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{n2}", rng.Next(0, 1000).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{n}",  rng.Next(1, 100).ToString(CultureInfo.InvariantCulture),  StringComparison.Ordinal);
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
