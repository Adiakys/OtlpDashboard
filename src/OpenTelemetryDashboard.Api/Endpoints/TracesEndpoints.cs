using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding target for <c>GET /api/v1/traces</c>.
/// </summary>
internal sealed record TraceQueryParameters(
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To,
    [FromQuery(Name = "limit")] int? Limit,
    [FromQuery(Name = "cursor")] string? Cursor,
    [FromQuery(Name = "service")] string? Service = null,
    // Drill-down for traces involving Resources without a `service.name`
    // (the "(unnamed)" service-map node has no string identity to pass
    // through `service`). When true, `service` is ignored.
    [FromQuery(Name = "noService")] bool? NoService = null,
    [FromQuery(Name = "status")] string? Status = null,
    [FromQuery(Name = "minMs")] double? MinMs = null,
    [FromQuery(Name = "maxMs")] double? MaxMs = null,
    [FromQuery(Name = "spanNameContains")] string? SpanNameContains = null,
    // Attribute filters as `key:value` pairs (any-span match,
    // AND across pairs). See <see cref="LogQueryParameters.Attr"/>.
    [FromQuery(Name = "attr")] string[]? Attr = null);

/// <summary>
/// Query-string binding target for <c>GET /api/v1/traces/aggregations</c>.
/// </summary>
internal sealed record TraceAggregationParameters(
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To,
    [FromQuery(Name = "limit")] int? Limit,
    [FromQuery(Name = "metric")] string? Metric = null,
    [FromQuery(Name = "service")] string? Service = null,
    [FromQuery(Name = "attr")] string[]? Attr = null);

/// <summary>
/// HTTP handlers for the trace-listing and trace-detail endpoints. Wiring
/// lives in <see cref="QueryApiExtensions.MapQueryApi"/>; this class holds
/// only the per-endpoint logic.
/// </summary>
internal static class TracesEndpoints
{
    public static async Task<Results<Ok<PagedResponse<TraceSummaryDto>>, ValidationProblem>> GetTracesAsync(
        [AsParameters] TraceQueryParameters parameters,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryBuildTraceQuery(parameters, options.Value, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var items = new List<TraceSummaryDto>(query.Limit);
        long lastStart = 0;
        long lastKey = 0;
        var overflowed = false;

        await foreach (var row in reader.QueryTraceSummariesAsync(query, cancellationToken).ConfigureAwait(false))
        {
            if (items.Count == query.Limit)
            {
                overflowed = true;
                break;
            }

            items.Add(row.Summary.ToDto(row.ServiceName));
            lastStart = row.Summary.StartUnixNano;
            lastKey = row.SecondaryKey;
        }

        var nextCursor = overflowed ? CursorCodec.EncodeTrace(lastStart, lastKey) : null;
        return TypedResults.Ok(new PagedResponse<TraceSummaryDto>(items, nextCursor));
    }

    public static async Task<Results<Ok<TraceAggregationsResponse>, ValidationProblem>> GetTraceAggregationsAsync(
        [AsParameters] TraceAggregationParameters parameters,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryBuildTraceAggregationQuery(parameters, options.Value, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var rows = await reader.AggregateTracesAsync(query, cancellationToken).ConfigureAwait(false);
        var items = new List<TraceAggregationItemDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(new TraceAggregationItemDto(row.Key, row.Count, row.ErrorCount, row.AvgMs, row.MaxMs));
        }
        return TypedResults.Ok(new TraceAggregationsResponse(items));
    }

    public static async Task<Results<Ok<TraceDetailDto>, NotFound, ValidationProblem>> GetTraceAsync(
        string traceId,
        ITraceReader reader,
        CancellationToken cancellationToken)
    {
        if (!TraceId.TryParse(traceId.AsSpan(), out var parsed))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["traceId"] = ["'traceId' must be a 32-character lowercase hex string."],
                });
        }

        var spans = new List<SpanDto>();
        await foreach (var row in reader.GetSpansInTraceAsync(parsed, cancellationToken).ConfigureAwait(false))
        {
            spans.Add(row.Span.ToDto(row.ServiceName));
        }

        if (spans.Count == 0)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new TraceDetailDto(parsed.ToString(), spans));
    }
}
