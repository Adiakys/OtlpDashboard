using System.Diagnostics.CodeAnalysis;
using OpenTelemetryDashboard.Api.Endpoints;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Api;

/// <summary>
/// Optional time window for metric point queries. Parsed out of
/// <c>from</c>/<c>to</c> query-string parameters; <c>null</c> means the
/// caller asked for the full ring-buffer snapshot.
/// </summary>
internal readonly record struct MetricPointsTimeWindow(DateTimeOffset From, DateTimeOffset To);

/// <summary>
/// Translates raw query-string parameters into validated
/// <see cref="LogQuery"/> / <see cref="TraceQuery"/> values, producing an
/// RFC 7807-shaped error dictionary on failure that endpoints hand back via
/// <c>TypedResults.ValidationProblem</c>.
/// </summary>
internal static class QueryValidation
{
    public static bool TryBuildLogQuery(
        LogQueryParameters parameters,
        QueryApiOptions options,
        [NotNullWhen(true)] out LogQuery? query,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (!TryValidateWindow(parameters.From, parameters.To, options, out var from, out var to, out errors))
        {
            query = null;
            return false;
        }

        if (!TryResolveLimit(parameters.Limit, options, out var limit, out errors))
        {
            query = null;
            return false;
        }

        CursorPosition? cursor = null;
        if (parameters.Cursor is not null)
        {
            if (!CursorCodec.TryDecodeLog(parameters.Cursor, out var decoded))
            {
                query = null;
                errors = SingleError("cursor", "The 'cursor' value is not a valid pagination token.");
                return false;
            }

            cursor = decoded;
        }

        TraceId? traceId = null;
        if (!string.IsNullOrEmpty(parameters.TraceId))
        {
            if (!TraceId.TryParse(parameters.TraceId.AsSpan(), out var parsed))
            {
                query = null;
                errors = SingleError("traceId", "'traceId' must be a 32-character lowercase hex string.");
                return false;
            }
            traceId = parsed;
        }

        var service = string.IsNullOrWhiteSpace(parameters.Service) ? null : parameters.Service;

        query = new LogQuery(from, to, limit, cursor, traceId, service);
        errors = null;
        return true;
    }

    public static bool TryBuildTraceQuery(
        TraceQueryParameters parameters,
        QueryApiOptions options,
        [NotNullWhen(true)] out TraceQuery? query,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (!TryValidateWindow(parameters.From, parameters.To, options, out var from, out var to, out errors))
        {
            query = null;
            return false;
        }

        if (!TryResolveLimit(parameters.Limit, options, out var limit, out errors))
        {
            query = null;
            return false;
        }

        CursorPosition? cursor = null;
        if (parameters.Cursor is not null)
        {
            if (!CursorCodec.TryDecodeTrace(parameters.Cursor, out var decoded))
            {
                query = null;
                errors = SingleError("cursor", "The 'cursor' value is not a valid pagination token.");
                return false;
            }

            cursor = decoded;
        }

        var service = string.IsNullOrWhiteSpace(parameters.Service) ? null : parameters.Service;

        query = new TraceQuery(from, to, limit, cursor, service);
        errors = null;
        return true;
    }

    public static bool TryBuildMetricPointsQuery(
        MetricPointsQueryParameters parameters,
        QueryApiOptions options,
        [NotNullWhen(true)] out InstrumentKey? key,
        out MetricPointsTimeWindow? window,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        key = null;
        window = null;

        var problems = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(parameters.ResourceHash))
        {
            problems["resourceHash"] = ["The 'resourceHash' query parameter is required."];
        }
        else if (!IsLowercaseHex(parameters.ResourceHash))
        {
            problems["resourceHash"] = ["'resourceHash' must be a lowercase hex string."];
        }

        if (parameters.ScopeName is null)
        {
            problems["scopeName"] = ["The 'scopeName' query parameter is required (use an empty value for the anonymous scope)."];
        }

        if (string.IsNullOrEmpty(parameters.InstrumentName))
        {
            problems["instrumentName"] = ["The 'instrumentName' query parameter is required."];
        }

        InstrumentKind kind = default;
        if (string.IsNullOrEmpty(parameters.Kind))
        {
            problems["kind"] = ["The 'kind' query parameter is required (e.g. 'Gauge', 'Sum')."];
        }
        else if (!Enum.TryParse(parameters.Kind, ignoreCase: true, out kind) || !Enum.IsDefined(kind))
        {
            problems["kind"] = [$"'kind' must be one of: {string.Join(", ", Enum.GetNames<InstrumentKind>())}."];
        }

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        if (parameters.From is not null || parameters.To is not null)
        {
            if (!TryValidateWindow(parameters.From, parameters.To, options, out var fromValue, out var toValue, out errors))
            {
                return false;
            }
            window = new MetricPointsTimeWindow(fromValue, toValue);
        }

        key = new InstrumentKey(
            parameters.ResourceHash!,
            parameters.ScopeName!,
            parameters.InstrumentName!,
            kind);

        errors = null;
        return true;
    }

    private static bool IsLowercaseHex(string value)
    {
        if (value.Length == 0 || value.Length % 2 != 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Thin wrapper around <see cref="TryValidateWindow"/> for endpoints that
    /// only need a time window (no cursor / no limit). Used by <c>/services</c>.
    /// </summary>
    public static bool TryValidateServicesWindow(
        ServicesQueryParameters parameters,
        QueryApiOptions options,
        out DateTimeOffset fromValue,
        out DateTimeOffset toValue,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        return TryValidateWindow(parameters.From, parameters.To, options, out fromValue, out toValue, out errors);
    }

    private static bool TryValidateWindow(
        DateTimeOffset? from,
        DateTimeOffset? to,
        QueryApiOptions options,
        out DateTimeOffset fromValue,
        out DateTimeOffset toValue,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        fromValue = default;
        toValue = default;
        var problems = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (from is null)
        {
            problems["from"] = ["The 'from' query parameter is required (ISO-8601 UTC date-time, e.g. 2026-04-23T12:00:00Z)."];
        }
        else if (from.Value.Offset != TimeSpan.Zero)
        {
            problems["from"] = ["The 'from' value must be in UTC; include a 'Z' suffix or '+00:00' offset."];
        }

        if (to is null)
        {
            problems["to"] = ["The 'to' query parameter is required (ISO-8601 UTC date-time, e.g. 2026-04-23T13:00:00Z)."];
        }
        else if (to.Value.Offset != TimeSpan.Zero)
        {
            problems["to"] = ["The 'to' value must be in UTC; include a 'Z' suffix or '+00:00' offset."];
        }

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        fromValue = from!.Value;
        toValue = to!.Value;

        if (fromValue >= toValue)
        {
            errors = SingleError("to", "'to' must be strictly greater than 'from'.");
            return false;
        }

        var window = toValue - fromValue;
        var max = TimeSpan.FromHours(options.MaxWindowHours);
        if (window > max)
        {
            errors = SingleError(
                "to",
                $"The requested time window ({window.TotalHours:F1}h) exceeds the configured maximum of {options.MaxWindowHours}h.");
            return false;
        }

        errors = null;
        return true;
    }

    private static bool TryResolveLimit(
        int? requested,
        QueryApiOptions options,
        out int limit,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (requested is null)
        {
            limit = options.DefaultLimit;
            errors = null;
            return true;
        }

        var value = requested.Value;
        if (value < 1)
        {
            limit = 0;
            errors = SingleError("limit", "'limit' must be greater than or equal to 1.");
            return false;
        }

        if (value > options.MaxLimit)
        {
            limit = 0;
            errors = SingleError("limit", $"'limit' must be less than or equal to {options.MaxLimit}.");
            return false;
        }

        limit = value;
        errors = null;
        return true;
    }

    private static Dictionary<string, string[]> SingleError(string key, string message) =>
        new(StringComparer.Ordinal) { [key] = [message] };
}
