using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Core.Metrics;
using OpenTelemetryDashboard.Persistence.Ingestion;
using OpenTelemetryDashboard.Persistence.Metrics.Entities;

namespace OpenTelemetryDashboard.Persistence.Sinks;

/// <summary>
/// Persists <see cref="MetricBatch"/> windows through EF Core. Resolves each
/// <see cref="InstrumentKey"/> to its surrogate <c>Id</c> via
/// <see cref="InstrumentCache"/>; rows missing from the cache are looked up
/// (or inserted) in a single SaveChanges round-trip alongside the points.
/// Resources are deduped through <see cref="ResourceCache"/>, mirroring the
/// trace and log sinks.
/// </summary>
public sealed class EfCoreMetricSink : IMetricSink
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly ResourceCache _resourceCache;
    private readonly InstrumentCache _instrumentCache;
    private readonly TelemetrySinkMetrics _metrics;
    private readonly ILogger<EfCoreMetricSink> _logger;

    public EfCoreMetricSink(
        IDbContextFactory<TelemetryDbContext> contextFactory,
        ResourceCache resourceCache,
        InstrumentCache instrumentCache,
        TelemetrySinkMetrics metrics,
        ILogger<EfCoreMetricSink> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(resourceCache);
        ArgumentNullException.ThrowIfNull(instrumentCache);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _resourceCache = resourceCache;
        _instrumentCache = instrumentCache;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task WriteAsync(IReadOnlyList<MetricBatch> batches, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (batches.Count == 0)
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // Pure Add path — see EfCoreTraceSink for the rationale.
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var resourcesByHash = new Dictionary<byte[], Resource>(ByteArrayEqualityComparer.Instance);
        var sampleCount = 0;

        foreach (var batch in batches)
        {
            sampleCount += batch.Samples.Count;
            foreach (var resource in batch.Resources)
            {
                resourcesByHash.TryAdd(resource.Hash, resource);
            }
        }

        if (sampleCount == 0)
        {
            return;
        }

        var pendingResourceCache = await ResourceUpserter
            .AddMissingAsync(context, resourcesByHash, _resourceCache, cancellationToken)
            .ConfigureAwait(false);

        // Pass 1 — resolve the InstrumentRecord for every distinct key. Cache
        // hits don't issue any queries; misses are looked up in a single IN
        // query, then any still-missing keys are inserted as new instrument
        // rows. We need their surrogate Ids before we can attach points, so
        // SaveChanges is called once after the inserts and before the points.
        var keysInBatch = new HashSet<InstrumentKey>();
        var idByKey = new Dictionary<InstrumentKey, long>();
        var newInstrumentByKey = new Dictionary<InstrumentKey, InstrumentRecord>();
        var sampleByKey = new Dictionary<InstrumentKey, MetricSample>();

        foreach (var batch in batches)
        {
            foreach (var sample in batch.Samples)
            {
                if (!keysInBatch.Add(sample.Key))
                {
                    continue;
                }

                if (_instrumentCache.TryGet(sample.Key, out var cachedId))
                {
                    idByKey[sample.Key] = cachedId;
                }
                else
                {
                    sampleByKey[sample.Key] = sample;
                }
            }
        }

        if (sampleByKey.Count > 0)
        {
            await ResolveOrCreateInstrumentsAsync(
                context, sampleByKey, idByKey, newInstrumentByKey, cancellationToken)
                .ConfigureAwait(false);
        }

        // Pass 2 — append point rows referencing the resolved Ids. New
        // instruments need their Ids available, so we save the dimension
        // table first if any inserts are pending.
        var pendingInstrumentCache = newInstrumentByKey;
        try
        {
            // Two SaveChanges with separate retries: the points have an FK on
            // InstrumentId, so the dimension save must commit before the point
            // Add() loop runs. Wrapping both in a single outer retry would
            // cause double-Add of the point rows on the second attempt; the
            // staged retry keeps the Add() pure once-only.
            if (newInstrumentByKey.Count > 0)
            {
                await BoundedRetry
                    .ExecuteAsync(ct => context.SaveChangesAsync(ct), cancellationToken)
                    .ConfigureAwait(false);
                foreach (var (key, record) in newInstrumentByKey)
                {
                    idByKey[key] = record.Id;
                }
            }

            foreach (var batch in batches)
            {
                foreach (var sample in batch.Samples)
                {
                    var instrumentId = idByKey[sample.Key];
                    context.MetricPoints.Add(MetricPointFromSample(instrumentId, sample.Point));
                }
            }

            await BoundedRetry
                .ExecuteAsync(ct => context.SaveChangesAsync(ct), cancellationToken)
                .ConfigureAwait(false);

            ResourceUpserter.CachePending(_resourceCache, pendingResourceCache);
            foreach (var (key, record) in pendingInstrumentCache)
            {
                _instrumentCache.Set(key, record.Id);
            }
            foreach (var (key, id) in idByKey)
            {
                _instrumentCache.Set(key, id);
            }

            _metrics.RecordMetricSuccess(sampleCount);
            _logger.MetricsPersisted(resourcesByHash.Count, idByKey.Count, sampleCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // See EfCoreTraceSink for the swallow rationale; drop is observable
            // through TelemetrySinkMetrics + /healthz.
            _metrics.RecordMetricDrop(sampleCount);
            _logger.MetricsBatchFailed(ex, sampleCount);
        }
    }

    private static MetricPointRecord MetricPointFromSample(long instrumentId, DataPoint point) =>
        new()
        {
            InstrumentId = instrumentId,
            TimeUnixNano = point.TimeUnixNano,
            StartTimeUnixNano = point.StartTimeUnixNano,
            Value = point.Value,
            Attributes = point.Attributes,
        };

    private static async Task ResolveOrCreateInstrumentsAsync(
        TelemetryDbContext context,
        Dictionary<InstrumentKey, MetricSample> missingByKey,
        Dictionary<InstrumentKey, long> idByKey,
        Dictionary<InstrumentKey, InstrumentRecord> newInstrumentByKey,
        CancellationToken cancellationToken)
    {
        // The unique index on (ResourceHash, ScopeName, Name, Kind) is the
        // natural key for an instrument. We can't translate the hex back to
        // bytes without per-hash work, but the count of missing keys per
        // batch is tiny (the distinct (resource, scope, name, kind) set
        // dedupes aggressively) so a single OR-chain is fine.
        var missingHashes = new HashSet<byte[]>(ByteArrayEqualityComparer.Instance);
        var missingScopeNames = new HashSet<string>(StringComparer.Ordinal);
        var missingNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in missingByKey.Keys)
        {
            missingHashes.Add(Convert.FromHexString(key.ResourceHashHex));
            missingScopeNames.Add(key.ScopeName);
            missingNames.Add(key.InstrumentName);
        }

        var hashList = missingHashes.ToList();
        var scopeList = missingScopeNames.ToList();
        var nameList = missingNames.ToList();

        var existing = await context.Instruments
            .AsNoTracking()
            .Where(i =>
                hashList.Contains(i.ResourceHash) &&
                scopeList.Contains(i.ScopeName) &&
                nameList.Contains(i.Name))
            .Select(i => new { i.Id, i.ResourceHash, i.ScopeName, i.Name, i.Kind })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var row in existing)
        {
            var hex = Convert.ToHexString(row.ResourceHash).ToLowerInvariant();
            var key = new InstrumentKey(hex, row.ScopeName, row.Name, row.Kind);
            if (missingByKey.ContainsKey(key))
            {
                idByKey[key] = row.Id;
                missingByKey.Remove(key);
            }
        }

        foreach (var (key, sample) in missingByKey)
        {
            var record = new InstrumentRecord
            {
                ResourceHash = Convert.FromHexString(key.ResourceHashHex),
                ScopeName = key.ScopeName,
                Name = key.InstrumentName,
                Kind = key.Kind,
                Description = sample.Instrument.Description,
                Unit = sample.Instrument.Unit,
                IsMonotonic = sample.Instrument.IsMonotonic,
                Temporality = sample.Instrument.Temporality,
            };
            context.Instruments.Add(record);
            newInstrumentByKey[key] = record;
        }
    }
}

internal static partial class EfCoreMetricSinkLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "EfCoreMetricSink persisted {Resources} resources, {Instruments} instruments, {Points} points")]
    public static partial void MetricsPersisted(this ILogger logger, int resources, int instruments, int points);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "EfCoreMetricSink dropped batch of {Points} points after exhausting retries")]
    public static partial void MetricsBatchFailed(this ILogger logger, Exception exception, int points);
}
