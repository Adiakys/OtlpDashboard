using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Endpoints;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;

namespace OpenTelemetryDashboard.Api.Mcp;

/// <summary>
/// MCP tools that expose the log-side of the Query API. Each method mirrors
/// the corresponding <c>GET /api/v1/logs*</c> endpoint: same parameters, same
/// validation (<see cref="QueryValidation"/>), same DTOs. Reader and options
/// are resolved from DI and therefore excluded from the tool's JSON schema.
/// </summary>
[McpServerToolType]
internal sealed class LogTools
{
    [McpServerTool(Name = "query_logs", ReadOnly = true, Idempotent = true)]
    [Description("List log records inside a UTC time window, ordered by timestamp descending. Supports keyset pagination via the 'cursor' returned in 'nextCursor'.")]
    public static async Task<PagedResponse<LogRecordDto>> QueryLogsAsync(
        [Description("Window start (ISO-8601 UTC, e.g. 2026-04-23T12:00:00Z). Required.")] DateTimeOffset from,
        [Description("Window end (ISO-8601 UTC). Must be strictly greater than 'from'. Required.")] DateTimeOffset to,
        ILogReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken,
        [Description("Maximum records returned. Defaults to QueryApi.DefaultLimit; capped at QueryApi.MaxLimit.")] int? limit = null,
        [Description("Opaque continuation token returned by a previous call. Pass to fetch the next page.")] string? cursor = null,
        [Description("Filter by trace id (32-char lowercase hex).")] string? traceId = null,
        [Description("Filter by service.name (exact match).")] string? service = null,
        [Description("Filter records with severity_number >= this value (OTLP 0-24).")] int? minSeverity = null)
    {
        var parameters = new LogQueryParameters(from, to, limit, cursor, traceId, service, minSeverity);
        if (!QueryValidation.TryBuildLogQuery(parameters, options.Value, out var query, out var errors))
        {
            throw new McpException(FormatValidationErrors(errors));
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
        return new PagedResponse<LogRecordDto>(items, nextCursor);
    }

    [McpServerTool(Name = "list_log_services", ReadOnly = true, Idempotent = true)]
    [Description("Distinct service.name values attached to log records inside the given UTC window. Sorted alphabetically.")]
    public static async Task<IReadOnlyList<string>> ListLogServicesAsync(
        [Description("Window start (ISO-8601 UTC). Required.")] DateTimeOffset from,
        [Description("Window end (ISO-8601 UTC). Required, > from.")] DateTimeOffset to,
        ILogReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        var parameters = new ServicesQueryParameters(from, to);
        if (!QueryValidation.TryValidateServicesWindow(parameters, options.Value, out var fromValue, out var toValue, out var errors))
        {
            throw new McpException(FormatValidationErrors(errors));
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        await foreach (var name in reader.GetDistinctServiceNamesAsync(fromValue, toValue, cancellationToken).ConfigureAwait(false))
        {
            names.Add(name);
        }

        return [.. names];
    }

    internal static string FormatValidationErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        var parts = errors.Select(kvp => $"{kvp.Key}: {string.Join("; ", kvp.Value)}");
        return "Validation failed. " + string.Join(" | ", parts);
    }
}
