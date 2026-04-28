using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Ingestion;

/// <summary>
/// Shared helper used by EF Core sinks to upsert <see cref="Resource"/> rows
/// with in-process caching. Correctness note: resources that are ADDED (not yet
/// persisted) are cached only AFTER a successful SaveChanges — caching them
/// optimistically would poison the cache on failure and cause FK violations on
/// the next batch.
/// </summary>
internal static class ResourceUpserter
{
    /// <summary>
    /// For each resource hash not in the cache:
    /// <list type="bullet">
    ///   <item>if already present in DB → add to the cache immediately;</item>
    ///   <item>if not present in DB → add to the DbContext and return it in the "pending" list so the caller can cache it after SaveChanges.</item>
    /// </list>
    /// </summary>
    public static async Task<IReadOnlyList<byte[]>> AddMissingAsync(
        TelemetryDbContext context,
        IReadOnlyDictionary<byte[], Resource> resourcesByHash,
        ResourceCache cache,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resourcesByHash);
        ArgumentNullException.ThrowIfNull(cache);

        if (resourcesByHash.Count == 0)
        {
            return [];
        }

        var unknownHashes = new List<byte[]>();
        foreach (var hash in resourcesByHash.Keys)
        {
            if (!cache.Contains(hash))
            {
                unknownHashes.Add(hash);
            }
        }

        if (unknownHashes.Count == 0)
        {
            return [];
        }

        var existing = await context.Resources
            .AsNoTracking()
            .Where(r => unknownHashes.Contains(r.Hash))
            .Select(r => r.Hash)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var existingSet = new HashSet<byte[]>(existing, ByteArrayEqualityComparer.Instance);
        var pendingCache = new List<byte[]>();

        foreach (var hash in unknownHashes)
        {
            if (existingSet.Contains(hash))
            {
                cache.Add(hash);
            }
            else
            {
                context.Resources.Add(resourcesByHash[hash]);
                pendingCache.Add(hash);
            }
        }

        return pendingCache;
    }

    public static void CachePending(ResourceCache cache, IReadOnlyList<byte[]> pendingHashes)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(pendingHashes);

        foreach (var hash in pendingHashes)
        {
            cache.Add(hash);
        }
    }
}
