using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.Persistence.Sinks;

/// <summary>
/// Persists <see cref="TraceBatch"/> windows through EF Core. Shares the
/// <see cref="ResourceCache"/> with the other EF sinks so Resource dedup is
/// effective across signals.
/// </summary>
public sealed class EfCoreTraceSink : ITraceSink
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly ResourceCache _resourceCache;
    private readonly TelemetrySinkMetrics _metrics;
    private readonly ILogger<EfCoreTraceSink> _logger;

    public EfCoreTraceSink(
        IDbContextFactory<TelemetryDbContext> contextFactory,
        ResourceCache resourceCache,
        TelemetrySinkMetrics metrics,
        ILogger<EfCoreTraceSink> logger)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(resourceCache);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);

        _contextFactory = contextFactory;
        _resourceCache = resourceCache;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task WriteAsync(IReadOnlyList<TraceBatch> batches, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batches);
        if (batches.Count == 0)
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        // Pure Add path — no entity mutated in place, so DetectChanges has
        // nothing to scan. Disabling it skips O(N) snapshots across spans
        // (each carrying an attribute map and owned events/links) per save.
        context.ChangeTracker.AutoDetectChangesEnabled = false;

        var resourcesByHash = new Dictionary<byte[], Resource>(ByteArrayEqualityComparer.Instance);
        var spanCount = 0;

        foreach (var batch in batches)
        {
            foreach (var resource in batch.Resources)
            {
                resourcesByHash.TryAdd(resource.Hash, resource);
            }
        }

        var pendingCache = await ResourceUpserter
            .AddMissingAsync(context, resourcesByHash, _resourceCache, cancellationToken)
            .ConfigureAwait(false);

        foreach (var batch in batches)
        {
            foreach (var span in batch.Spans)
            {
                context.Spans.Add(span);
                spanCount++;
            }
        }

        try
        {
            await BoundedRetry
                .ExecuteAsync(ct => context.SaveChangesAsync(ct), cancellationToken)
                .ConfigureAwait(false);
            ResourceUpserter.CachePending(_resourceCache, pendingCache);
            _metrics.RecordTraceSuccess(spanCount);
            _logger.TracesPersisted(resourcesByHash.Count, spanCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Last-resort swallow: rethrowing would crash the BackgroundService
            // and lose every batch still in the channel. The drop is now
            // observable through TelemetrySinkMetrics + /healthz.
            _metrics.RecordTraceDrop(spanCount);
            _logger.TracesBatchFailed(ex, spanCount);
        }
    }
}

internal static partial class EfCoreTraceSinkLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "EfCoreTraceSink persisted {Resources} resources, {Spans} spans")]
    public static partial void TracesPersisted(this ILogger logger, int resources, int spans);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "EfCoreTraceSink dropped batch of {Spans} spans after exhausting retries")]
    public static partial void TracesBatchFailed(this ILogger logger, Exception exception, int spans);
}
