using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Abstractions;

/// <summary>
/// Read-side contract for log-record storage. Implementations MUST use
/// read-only (no-tracking) semantics.
/// </summary>
public interface ILogReader
{
    /// <summary>
    /// Returns up to <c>query.Limit + 1</c> records within the time window,
    /// ordered by <c>(TimeUnixNano, SecondaryKey)</c> descending. The extra
    /// record lets callers detect the need for a continuation cursor. The
    /// yielded <c>SecondaryKey</c> is a storage-provided long the caller
    /// round-trips via <see cref="CursorPosition"/>. <c>ServiceName</c> is the
    /// associated resource's <c>service.name</c> (null when unset).
    /// </summary>
    IAsyncEnumerable<(LogRecord Record, long SecondaryKey, string? ServiceName)> QueryAsync(
        LogQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the distinct (<c>service.name</c>, <c>service.instance.id</c>)
    /// pairs attached to log records inside the given window, skipping
    /// resources without a <c>service.name</c>. Used to populate the UI filter.
    /// </summary>
    IAsyncEnumerable<(string ServiceName, string? InstanceId)> GetDistinctServicesAsync(
        DateTimeOffset fromTime,
        DateTimeOffset toTime,
        CancellationToken cancellationToken);
}
