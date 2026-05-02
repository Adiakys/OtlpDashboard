using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace SampleServer;

/// <summary>
/// Tiny domain service driving the counter row in postgres through
/// HybridCache (Redis-backed). Wraps every mutation in a custom
/// `ActivitySource` so the dashboard sees one trace per request, and
/// publishes a custom counter via the SDK Meter so the dashboard's
/// metrics page has at least one app-emitted instrument.
/// </summary>
public sealed class CounterService
{
    public const string ActivitySourceName = "SampleServer.Counter";
    public const string MeterName = "SampleServer";

    private static readonly ActivitySource Activity = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private readonly Counter<long> _mutationsTotal =
        Meter.CreateCounter<long>("sample_server.counter.mutations", unit: "1",
            description: "Number of counter mutations served.");

    private readonly Counter<long> _readsTotal =
        Meter.CreateCounter<long>("sample_server.counter.reads", unit: "1",
            description: "Number of counter reads served.");

    private readonly Histogram<double> _operationLatency =
        Meter.CreateHistogram<double>("sample_server.counter.operation_latency", unit: "ms",
            description: "End-to-end latency of counter operations.");

    private const string CacheKey = "counter:1";

    private readonly HybridCache _cache;
    private readonly IDbContextFactory<CounterDbContext> _ctxFactory;
    private readonly ILogger<CounterService> _logger;

    public CounterService(
        HybridCache cache,
        IServiceProvider sp,
        ILogger<CounterService> logger)
    {
        _cache = cache;
        // The pooled context factory is registered automatically by
        // AddDbContextPool — pull it from the provider so we don't add
        // ctor noise.
        _ctxFactory = sp.GetRequiredService<IDbContextFactory<CounterDbContext>>();
        _logger = logger;
    }

    public async Task<int> GetAsync(CancellationToken ct)
    {
        using var act = Activity.StartActivity("counter.get");
        var sw = Stopwatch.StartNew();
        try
        {
            var value = await _cache.GetOrCreateAsync(CacheKey,
                async cancellation =>
                {
                    using var dbAct = Activity.StartActivity("counter.db.read");
                    await using var ctx = await _ctxFactory.CreateDbContextAsync(cancellation);
                    var row = await ctx.Counters.FirstOrDefaultAsync(c => c.Id == 1, cancellation);
                    return row?.Value ?? 0;
                },
                cancellationToken: ct);
            _readsTotal.Add(1);
            return value;
        }
        finally
        {
            _operationLatency.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("op", "get"));
        }
    }

    public async Task<int> SetAsync(int newValue, CancellationToken ct)
    {
        using var act = Activity.StartActivity("counter.set");
        act?.SetTag("counter.new_value", newValue);
        var sw = Stopwatch.StartNew();
        try
        {
            await using var ctx = await _ctxFactory.CreateDbContextAsync(ct);
            var row = await ctx.Counters.FirstAsync(c => c.Id == 1, ct);
            row.Value = newValue;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await ctx.SaveChangesAsync(ct);

            await _cache.SetAsync(CacheKey, newValue, cancellationToken: ct);
            _mutationsTotal.Add(1, new KeyValuePair<string, object?>("op", "set"));
            _logger.LogInformation("counter set to {Value}", newValue);
            return newValue;
        }
        finally
        {
            _operationLatency.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("op", "set"));
        }
    }

    public async Task<(int oldValue, int newValue, int delta)> MutateRandomAsync(CancellationToken ct)
    {
        using var act = Activity.StartActivity("counter.mutate.random");
        var sw = Stopwatch.StartNew();
        try
        {
            var current = await GetAsync(ct);
            var delta = Random.Shared.Next(-10, 11);
            var next = current + delta;
            await SetAsync(next, ct);
            _mutationsTotal.Add(1, new KeyValuePair<string, object?>("op", "random"));
            act?.SetTag("counter.delta", delta);
            return (current, next, delta);
        }
        finally
        {
            _operationLatency.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("op", "random"));
        }
    }
}
