using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Endpoints;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Api.Mcp;

/// <summary>
/// MCP tools exposing the trace-side of the Query API. Mirrors
/// <c>GET /api/v1/traces*</c> 1:1.
/// </summary>
[McpServerToolType]
internal sealed class TraceTools
{
    [McpServerTool(Name = "query_traces", ReadOnly = true, Idempotent = true)]
    [Description("List trace summaries inside a UTC time window, ordered by start time descending. Supports keyset pagination via 'cursor'/'nextCursor'.")]
    public static async Task<PagedResponse<TraceSummaryDto>> QueryTracesAsync(
        [Description("Window start (ISO-8601 UTC). Required.")] DateTimeOffset from,
        [Description("Window end (ISO-8601 UTC). Required, > from.")] DateTimeOffset to,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken,
        [Description("Maximum traces returned. Defaults to QueryApi.DefaultLimit; capped at QueryApi.MaxLimit.")] int? limit = null,
        [Description("Opaque continuation token from a previous call.")] string? cursor = null,
        [Description("Filter by service.name (exact match on root span's resource).")] string? service = null)
    {
        var parameters = new TraceQueryParameters(
            from, to, limit, cursor,
            Services: service is null ? null : [service]);
        if (!QueryValidation.TryBuildTraceQuery(parameters, options.Value, out var query, out var errors))
        {
            throw new McpException(LogTools.FormatValidationErrors(errors));
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
        return new PagedResponse<TraceSummaryDto>(items, nextCursor);
    }

    [McpServerTool(Name = "get_trace", ReadOnly = true, Idempotent = true)]
    [Description("Return every span belonging to the given trace id, with each span's resource service.name. Errors if no spans match. Truncated=true means the trace exceeded the per-trace span cap and the spans returned are an early prefix.")]
    public static async Task<TraceDetailDto> GetTraceAsync(
        [Description("Trace id (32-character lowercase hex).")] string traceId,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!TraceId.TryParse(traceId.AsSpan(), out var parsed))
        {
            throw new McpException("traceId: must be a 32-character lowercase hex string.");
        }

        var snapshot = await reader
            .GetSpansInTraceAsync(parsed, options.Value.MaxSpansPerTrace, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.Spans.Count == 0)
        {
            throw new McpException($"No trace found with id '{parsed}'.");
        }

        var spans = new List<SpanDto>(snapshot.Spans.Count);
        foreach (var row in snapshot.Spans)
        {
            spans.Add(row.Span.ToDto(row.ServiceName));
        }

        return new TraceDetailDto(parsed.ToString(), spans, snapshot.Truncated);
    }

    [McpServerTool(Name = "list_trace_services", ReadOnly = true, Idempotent = true)]
    [Description("Distinct service.name values attached to traces whose earliest span falls inside the given UTC window. Sorted alphabetically.")]
    public static async Task<IReadOnlyList<string>> ListTraceServicesAsync(
        [Description("Window start (ISO-8601 UTC). Required.")] DateTimeOffset from,
        [Description("Window end (ISO-8601 UTC). Required, > from.")] DateTimeOffset to,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        var parameters = new ServicesQueryParameters(from, to);
        if (!QueryValidation.TryValidateServicesWindow(parameters, options.Value, out var fromValue, out var toValue, out var errors))
        {
            throw new McpException(LogTools.FormatValidationErrors(errors));
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        await foreach (var name in reader.GetDistinctServiceNamesAsync(fromValue, toValue, cancellationToken).ConfigureAwait(false))
        {
            names.Add(name);
        }

        return [.. names];
    }
}
