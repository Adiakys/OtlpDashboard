using OpenTelemetryDashboard.Core.Abstractions.Queries;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Read-side contract for service-map aggregation. Treated as a peer
/// of <see cref="ITraceReader"/> / <see cref="ILogReader"/> /
/// <see cref="IMetricReader"/>: callers ask for a graph of services
/// and dependencies, the implementation chooses how to satisfy that
/// (today: by querying spans). Storage details live behind the seam.
/// </summary>
public interface IServiceMapReader
{
    /// <summary>
    /// Returns distinct services touched in the window plus the
    /// cross-service call edges between them. When
    /// <see cref="ServiceMapQuery.ServiceName"/> is set, the result is
    /// narrowed to that service and its direct neighbours (focus
    /// mode). Implementations MUST use read-only (no-tracking)
    /// semantics.
    /// </summary>
    Task<ServiceMapResult> GetServiceMapAsync(
        ServiceMapQuery query,
        CancellationToken cancellationToken);
}
