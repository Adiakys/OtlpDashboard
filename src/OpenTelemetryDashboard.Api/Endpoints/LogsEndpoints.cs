using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding target for <c>GET /api/v1/logs</c>.
/// </summary>
internal sealed record LogQueryParameters(
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To,
    [FromQuery(Name = "limit")] int? Limit,
    [FromQuery(Name = "cursor")] string? Cursor,
    [FromQuery(Name = "traceId")] string? TraceId = null,
    // See <see cref="TraceQueryParameters.Services"/>.
    [FromQuery(Name = "services")] string[]? Services = null,
    [FromQuery(Name = "minSeverity")] int? MinSeverity = null,
    // Comma-separated bucket names (`trace,debug,info,warn,error,fatal`).
    // Multi-value query strings (`?severities=info&severities=warn`) also
    // bind because ASP.NET treats repeated keys as a list — the validator
    // accepts both shapes.
    [FromQuery(Name = "severities")] string[]? Severities = null,
    [FromQuery(Name = "bodyContains")] string? BodyContains = null,
    // Attribute filters as `key:value` pairs. Repeated keys are AND'd:
    // `?attr=http.route:/foo&attr=demo.scenario:post_counter_value`.
    // String-typed match only — see <see cref="AttributeFilter"/>.
    [FromQuery(Name = "attr")] string[]? Attr = null);

/// <summary>
/// HTTP handler(s) for the log-listing endpoint. Wiring (path, method, name)
/// lives in <see cref="QueryApiExtensions.MapQueryApi"/>; this class holds
/// only the per-endpoint logic.
/// </summary>
internal static class LogsEndpoints
{
    public static async Task<Results<Ok<PagedResponse<LogRecordDto>>, ValidationProblem>> GetLogsAsync(
        [AsParameters] LogQueryParameters parameters,
        ILogReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryBuildLogQuery(parameters, options.Value, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var items = new List<LogRecordDto>(query.Limit);
        long lastTime = 0;
        long lastKey = 0;
        var overflowed = false;

        await foreach (var row in reader.QueryAsync(query, cancellationToken).ConfigureAwait(false))
        {
            if (items.Count == query.Limit)
            {
                overflowed = true;
                break;
            }

            items.Add(row.Record.ToDto(row.ServiceName));
            lastTime = row.Record.TimeUnixNano;
            lastKey = row.SecondaryKey;
        }

        var nextCursor = overflowed ? CursorCodec.EncodeLog(lastTime, lastKey) : null;
        return TypedResults.Ok(new PagedResponse<LogRecordDto>(items, nextCursor));
    }
}
