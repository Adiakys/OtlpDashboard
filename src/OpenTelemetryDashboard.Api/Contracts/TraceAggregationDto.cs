namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Wire shape of a single Top-N aggregation row. Mirrors
/// <see cref="OpenTelemetryDashboard.Core.Abstractions.Queries.TraceAggregationRow"/>;
/// the API surfaces all four metrics so the SPA can re-sort
/// client-side (cheap) without a refetch.
/// </summary>
public sealed record TraceAggregationItemDto(
    string Key,
    long Count,
    long ErrorCount,
    double AvgMs,
    double MaxMs);

/// <summary>
/// Container for the aggregation response. Kept tiny on purpose — the
/// limit cap is small (≤100), a flat array is more honest than a
/// paged envelope.
/// </summary>
public sealed record TraceAggregationsResponse(IReadOnlyList<TraceAggregationItemDto> Items);
