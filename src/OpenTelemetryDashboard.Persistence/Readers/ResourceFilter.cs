using Microsoft.EntityFrameworkCore;

namespace OpenTelemetryDashboard.Persistence.Readers;

internal static class ResourceFilter
{
    /// <summary>
    /// Subquery of the resource hashes whose <c>service.name</c> or
    /// <c>service.name:service.instance.id</c> is in the list; <c>null</c> when empty.
    /// </summary>
    public static IQueryable<byte[]>? MatchingHashes(TelemetryDbContext context, IReadOnlyList<string>? serviceNames)
    {
        if (serviceNames is not { Count: > 0 } names)
        {
            return null;
        }

        return context.Resources
            .AsNoTracking()
            .Where(r => r.ServiceName != null && (
                names.Contains(r.ServiceName) ||
                (r.ServiceInstanceId != null && names.Contains(r.ServiceName + ":" + r.ServiceInstanceId))))
            .Select(r => r.Hash);
    }
}
