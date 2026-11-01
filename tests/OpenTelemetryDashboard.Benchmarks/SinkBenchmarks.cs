using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Ingestion.Translators;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Ingestion;
using OpenTelemetryDashboard.Persistence.PostgreSql;
using OpenTelemetryDashboard.Persistence.Sqlite;

namespace OpenTelemetryDashboard.Benchmarks;

/// <summary>
/// Time for one sink flush, the unit of work the background writer runs.
/// PostgreSQL is included only when <c>BENCH_POSTGRES</c> holds a connection
/// string to a database the benchmark may drop and recreate.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, warmupCount: 3, iterationCount: 20)]
public class SinkBenchmarks
{
    private const string PostgresEnvVar = "BENCH_POSTGRES";

    private readonly OtlpTraceTranslator _traceTranslator = new(NullLogger<OtlpTraceTranslator>.Instance);
    private readonly OtlpLogTranslator _logTranslator = new(NullLogger<OtlpLogTranslator>.Instance);
    private readonly OtlpMetricTranslator _metricTranslator = new(NullLogger<OtlpMetricTranslator>.Instance);
    private readonly OtlpPayloadFactory _factory = new(seed: 7, serviceCount: 10);

    private ServiceProvider _services = null!;
    private ITraceSink _traceSink = null!;
    private ILogSink _logSink = null!;
    private IMetricSink _metricSink = null!;
    private TelemetrySinkMetrics _sinkMetrics = null!;
    private string? _sqlitePath;

    private TraceBatch[] _traceBatches = [];
    private LogBatch[] _logBatches = [];
    private MetricBatch[] _metricBatches = [];

    [ParamsSource(nameof(Providers))]
    public string Provider { get; set; } = "Sqlite";

    [Params(512, 4096)]
    public int Items { get; set; }

    public static IEnumerable<string> Providers()
    {
        yield return "Sqlite";
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresEnvVar)))
        {
            yield return "PostgreSql";
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        if (Provider == "PostgreSql")
        {
            services.AddPostgreSqlTelemetryStore(Environment.GetEnvironmentVariable(PostgresEnvVar)!);
        }
        else
        {
            _sqlitePath = Path.Combine(Path.GetTempPath(), $"oteldash-bench-{Guid.NewGuid():N}.db");
            services.AddSqliteTelemetryStore($"Data Source={_sqlitePath}");
        }

        _services = services.BuildServiceProvider();

        using (var context = _services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>().CreateDbContext())
        {
            context.Database.EnsureDeleted();
            context.Database.Migrate();
        }

        _traceSink = _services.GetRequiredService<ITraceSink>();
        _logSink = _services.GetRequiredService<ILogSink>();
        _metricSink = _services.GetRequiredService<IMetricSink>();
        _sinkMetrics = _services.GetRequiredService<TelemetrySinkMetrics>();
    }

    [IterationSetup]
    public void PrepareBatches()
    {
        // The writer hands each sink a window of OTLP requests; 64 spans per
        // request mirrors a busy SDK exporter flushing small batches.
        const int perRequest = 64;
        var now = DateTimeOffset.UtcNow;
        var requests = Math.Max(1, Items / perRequest);
        _traceBatches = new TraceBatch[requests];
        _logBatches = new LogBatch[requests];
        _metricBatches = new MetricBatch[requests];
        for (var i = 0; i < requests; i++)
        {
            _traceBatches[i] = _traceTranslator.Translate(_factory.CreateTraces(perRequest, now))!;
            _logBatches[i] = _logTranslator.Translate(_factory.CreateLogs(perRequest, now))!;
            _metricBatches[i] = _metricTranslator.Translate(_factory.CreateMetrics(perRequest, now))!;
        }
    }

    [IterationCleanup]
    public void AssertNoDrops()
    {
        var snapshot = _sinkMetrics.Snapshot();
        if (snapshot.TracesDropped + snapshot.LogsDropped + snapshot.MetricsDropped > 0)
        {
            throw new InvalidOperationException($"Sink dropped data: {snapshot}");
        }
    }

    [Benchmark]
    public Task WriteSpans() => _traceSink.WriteAsync(_traceBatches, CancellationToken.None);

    [Benchmark]
    public Task WriteLogs() => _logSink.WriteAsync(_logBatches, CancellationToken.None);

    [Benchmark]
    public Task WriteMetricPoints() => _metricSink.WriteAsync(_metricBatches, CancellationToken.None);

    [GlobalCleanup]
    public void Cleanup()
    {
        _services.Dispose();
        if (_sqlitePath is not null)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _sqlitePath, _sqlitePath + "-wal", _sqlitePath + "-shm" })
            {
                File.Delete(file);
            }
        }
    }
}
