using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.Persistence.Ingestion;

namespace OpenTelemetryDashboard.Persistence.Sinks;

/// <summary>
/// Persists <see cref="LogBatch"/> windows through EF Core. Shares the
/// <see cref="ResourceCache"/> with the other EF sinks.
/// </summary>
public sealed class EfCoreLogSink : ILogSink
{
    private readonly IDbContextFactory<TelemetryDbContext> _contextFactory;
    private readonly ResourceCache _resourceCache;
    private readonly TelemetrySinkMetrics _metrics;
    private readonly ILogger<EfCoreLogSink> _logger;

    public EfCoreLogSink(
        IDbContextFactory<TelemetryDbContext> contextFactory,
        ResourceCache resourceCache,
        TelemetrySinkMetrics metrics,
        ILogger<EfCoreLogSink> logger)
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

    public async Task WriteAsync(IReadOnlyList<LogBatch> batches, CancellationToken cancellationToken)
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
        var logCount = 0;

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
            foreach (var record in batch.Records)
            {
                context.Logs.Add(record);
                logCount++;
            }
        }

        try
        {
            await BoundedRetry
                .ExecuteAsync(ct => context.SaveChangesAsync(ct), cancellationToken)
                .ConfigureAwait(false);
            ResourceUpserter.CachePending(_resourceCache, pendingCache);
            _metrics.RecordLogSuccess(logCount);
            _logger.LogsPersisted(resourcesByHash.Count, logCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // See EfCoreTraceSink for the swallow rationale; drop is observable
            // through TelemetrySinkMetrics + /healthz.
            _metrics.RecordLogDrop(logCount);
            _logger.LogsBatchFailed(ex, logCount);
        }
    }
}

internal static partial class EfCoreLogSinkLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "EfCoreLogSink persisted {Resources} resources, {Logs} logs")]
    public static partial void LogsPersisted(this ILogger logger, int resources, int logs);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error,
        Message = "EfCoreLogSink dropped batch of {Logs} logs after exhausting retries")]
    public static partial void LogsBatchFailed(this ILogger logger, Exception exception, int logs);
}
