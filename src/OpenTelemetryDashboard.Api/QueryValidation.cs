using System.Diagnostics.CodeAnalysis;
using OpenTelemetryDashboard.Api.Endpoints;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Api;

/// <summary>
/// Optional time window for metric point queries. Parsed out of
/// <c>from</c>/<c>to</c> query-string parameters; <c>null</c> means the
/// caller asked for the full series.
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

        // OTLP severity_number is in [0, 24]; anything beyond is the caller's
        // mistake. 0/null disables the filter, so we only validate the upper
        // bound — the reader skips zero-or-negative values.
        if (parameters.MinSeverity is { } minSev && minSev > 24)
        {
            query = null;
            errors = SingleError("minSeverity", "'minSeverity' must be in the range 0–24 (OTLP severity_number).");
            return false;
        }

        if (!TryExpandSeverityBuckets(parameters.Severities, out var severityNumbers, out errors))
        {
            query = null;
            return false;
        }

        var bodyContains = string.IsNullOrWhiteSpace(parameters.BodyContains) ? null : parameters.BodyContains;

        if (!TryParseAttributeFilters(parameters.Attr, out var logAttrFilters, out errors))
        {
            query = null;
            return false;
        }

        query = new LogQuery(
            from, to, limit, cursor, traceId, service, parameters.MinSeverity,
            severityNumbers, bodyContains, logAttrFilters);
        errors = null;
        return true;
    }

    /// <summary>
    /// Expand the SPA's severity-bucket selection into the matching set of
    /// OTLP severity numbers. Comma-separated values inside a single
    /// query-string entry are also accepted (`?severities=warn,error`).
    /// Returns <c>null</c> for the bucket list when no buckets are supplied —
    /// the reader treats null as "no filter".
    /// </summary>
    internal static bool TryExpandSeverityBuckets(
        string[]? buckets,
        out IReadOnlyList<int>? severityNumbers,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (buckets is null || buckets.Length == 0)
        {
            severityNumbers = null;
            errors = null;
            return true;
        }

        var numbers = new SortedSet<int>();
        foreach (var raw in buckets)
        {
            if (raw is null) continue;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var lower = part.ToLowerInvariant();
                var (lo, hi) = lower switch
                {
                    "trace" => (1, 4),
                    "debug" => (5, 8),
                    "info" => (9, 12),
                    "warn" or "warning" => (13, 16),
                    "error" => (17, 20),
                    "fatal" => (21, 24),
                    _ => (-1, -1),
                };
                if (lo < 0)
                {
                    severityNumbers = null;
                    errors = SingleError(
                        "severities",
                        "'severities' values must be from: trace, debug, info, warn, error, fatal.");
                    return false;
                }
                for (var n = lo; n <= hi; n++) numbers.Add(n);
            }
        }

        severityNumbers = numbers.Count == 0 ? null : [.. numbers];
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

        TraceStatusFilter? status = null;
        if (!string.IsNullOrWhiteSpace(parameters.Status))
        {
            var lowered = parameters.Status.Trim().ToLowerInvariant();
            status = lowered switch
            {
                "ok" => TraceStatusFilter.Ok,
                "error" => TraceStatusFilter.Error,
                "any" or "" => null,
                _ => (TraceStatusFilter?)(-1),
            };
            if (status == (TraceStatusFilter)(-1))
            {
                query = null;
                errors = SingleError("status", "'status' must be one of: any, ok, error.");
                return false;
            }
        }

        if (parameters.MinMs is { } minMs && minMs < 0)
        {
            query = null;
            errors = SingleError("minMs", "'minMs' must be greater than or equal to 0.");
            return false;
        }
        if (parameters.MaxMs is { } maxMs && maxMs < 0)
        {
            query = null;
            errors = SingleError("maxMs", "'maxMs' must be greater than or equal to 0.");
            return false;
        }
        if (parameters.MinMs is { } a && parameters.MaxMs is { } b && a > b)
        {
            query = null;
            errors = SingleError("maxMs", "'maxMs' must be greater than or equal to 'minMs'.");
            return false;
        }

        var spanName = string.IsNullOrWhiteSpace(parameters.SpanNameContains) ? null : parameters.SpanNameContains;

        if (!TryParseAttributeFilters(parameters.Attr, out var traceAttrFilters, out errors))
        {
            query = null;
            return false;
        }

        query = new TraceQuery(
            from, to, limit, cursor, service, status, parameters.MinMs, parameters.MaxMs,
            spanName, traceAttrFilters);
        errors = null;
        return true;
    }

    /// <summary>
    /// Parse <c>attr=key:value</c> entries (or comma-separated within one
    /// entry) into validated <see cref="AttributeFilter"/> pairs. Whitespace
    /// is trimmed; empty pairs are silently dropped; malformed entries
    /// (no colon, empty key/value, key with disallowed chars) produce a 400.
    /// Returns <c>null</c> when no filters were supplied — readers treat
    /// that as "no filter".
    /// <para>
    /// Keys are restricted to OTel-style identifiers
    /// (<c>[a-zA-Z][a-zA-Z0-9._-]*</c>). The strict allow-list lets the
    /// readers safely embed the key into a JSON path string at query
    /// time without any escaping concerns — the chars that would
    /// otherwise require careful path-syntax escaping (<c>"</c>, <c>$</c>,
    /// backslash, brackets) never reach the SQL layer.
    /// </para>
    /// </summary>
    internal static bool TryParseAttributeFilters(
        string[]? raw,
        out IReadOnlyList<AttributeFilter>? filters,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (raw is null || raw.Length == 0)
        {
            filters = null;
            errors = null;
            return true;
        }

        var list = new List<AttributeFilter>(raw.Length);
        foreach (var entry in raw)
        {
            if (entry is null) continue;
            // Multi-pair-per-entry support: `?attr=a:1,b:2` is the same
            // as `?attr=a:1&attr=b:2`. The single-key form remains the
            // primary interface; the comma form is just URL ergonomics.
            foreach (var part in entry.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var colon = part.IndexOf(':', StringComparison.Ordinal);
                if (colon < 0)
                {
                    filters = null;
                    errors = SingleError("attr", "'attr' values must be in the form 'key:value'.");
                    return false;
                }
                var key = part[..colon].Trim();
                var value = part[(colon + 1)..].Trim();
                if (key.Length == 0 || value.Length == 0)
                {
                    filters = null;
                    errors = SingleError("attr", "'attr' key and value must both be non-empty.");
                    return false;
                }
                if (!IsValidAttributeKey(key))
                {
                    filters = null;
                    errors = SingleError(
                        "attr",
                        "'attr' keys must match [a-zA-Z][a-zA-Z0-9._-]*.");
                    return false;
                }
                list.Add(new AttributeFilter(key, value));
            }
        }

        filters = list.Count == 0 ? null : list;
        errors = null;
        return true;
    }

    private static bool IsValidAttributeKey(string key)
    {
        if (key.Length == 0) return false;
        var first = key[0];
        if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z'))) return false;
        for (var i = 1; i < key.Length; i++)
        {
            var c = key[i];
            var ok = (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '.' || c == '_' || c == '-';
            if (!ok) return false;
        }
        return true;
    }

    public static bool TryBuildTraceAggregationQuery(
        TraceAggregationParameters parameters,
        QueryApiOptions options,
        [NotNullWhen(true)] out TraceAggregationQuery? query,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (!TryValidateWindow(parameters.From, parameters.To, options, out var from, out var to, out errors))
        {
            query = null;
            return false;
        }

        // Aggregations cap at 100 — Top-N is the use case, anything
        // above isn't actionable visually and would slow the GROUP BY
        // for no gain.
        var limit = parameters.Limit ?? 10;
        if (limit < 1 || limit > 100)
        {
            query = null;
            errors = SingleError("limit", "'limit' must be between 1 and 100 for aggregations.");
            return false;
        }

        var metric = TraceAggregationMetric.Count;
        if (!string.IsNullOrWhiteSpace(parameters.Metric))
        {
            metric = parameters.Metric.Trim().ToLowerInvariant() switch
            {
                "count" => TraceAggregationMetric.Count,
                "errorrate" or "error_rate" => TraceAggregationMetric.ErrorRate,
                "avgms" or "avg_ms" or "avg" => TraceAggregationMetric.AvgMs,
                "maxms" or "max_ms" or "max" => TraceAggregationMetric.MaxMs,
                _ => (TraceAggregationMetric)(-1),
            };
            if ((int)metric < 0)
            {
                query = null;
                errors = SingleError("metric", "'metric' must be one of: count, errorRate, avgMs, maxMs.");
                return false;
            }
        }

        var service = string.IsNullOrWhiteSpace(parameters.Service) ? null : parameters.Service;

        if (!TryParseAttributeFilters(parameters.Attr, out var attrFilters, out errors))
        {
            query = null;
            return false;
        }

        query = new TraceAggregationQuery(from, to, limit, metric, service, attrFilters);
        errors = null;
        return true;
    }

    public static bool TryBuildServiceMapQuery(
        ServiceMapParameters parameters,
        QueryApiOptions options,
        [NotNullWhen(true)] out ServiceMapQuery? query,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        if (!TryValidateWindow(parameters.From, parameters.To, options, out var from, out var to, out errors))
        {
            query = null;
            return false;
        }

        var service = string.IsNullOrWhiteSpace(parameters.Service) ? null : parameters.Service;
        query = new ServiceMapQuery(from, to, service);
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
