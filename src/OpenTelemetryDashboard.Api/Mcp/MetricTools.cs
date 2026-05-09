using System.ComponentModel;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Endpoints;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Api.Mcp;

/// <summary>
/// MCP tools exposing the metric-side of the Query API. Mirrors
/// <c>GET /api/v1/metrics*</c> 1:1.
/// </summary>
[McpServerToolType]
internal sealed class MetricTools
{
    [McpServerTool(Name = "list_metrics", ReadOnly = true, Idempotent = true)]
    [Description("Every instrument currently in the store, with point count and originating service. The four-field key (resourceHash, scopeName, name, kind) identifies a single time-series for query_metric_points.")]
    public static async Task<IReadOnlyList<InstrumentDto>> ListInstrumentsAsync(
        IMetricReader reader,
        CancellationToken cancellationToken)
    {
        var summaries = await reader.ListInstrumentsAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<InstrumentDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            items.Add(summary.Instrument.ToDto(summary.Key, summary.PointCount, summary.ServiceName, summary.ServiceInstanceId));
        }
        return items;
    }

    [McpServerTool(Name = "query_metric_points", ReadOnly = true, Idempotent = true)]
    [Description("Return the points of a single instrument time-series identified by (resourceHash, scopeName, instrumentName, kind) inside the required UTC window. Use list_metrics first to discover the four-field key. Truncated=true means the window contains more points than the configured cap and the points returned are an early prefix.")]
    public static async Task<MetricSeriesDto> QueryMetricPointsAsync(
        [Description("Resource hash (lowercase hex) — see list_metrics output.")] string resourceHash,
        [Description("Scope name (use empty string for the anonymous scope).")] string scopeName,
        [Description("Instrument name.")] string instrumentName,
        [Description("Instrument kind: Gauge, Sum, Histogram, ExponentialHistogram, Summary.")] string kind,
        [Description("Window start (ISO-8601 UTC). Required.")] DateTimeOffset from,
        [Description("Window end (ISO-8601 UTC). Required, > from.")] DateTimeOffset to,
        IMetricReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken,
        [Description("Set true to hydrate per-point attribute maps. Default false (cheaper for single-value widgets).")] bool? includeAttributes = null)
    {
        var parameters = new MetricPointsQueryParameters(resourceHash, scopeName, instrumentName, kind, from, to, includeAttributes);
        if (!QueryValidation.TryBuildMetricPointsQuery(parameters, options.Value, out var key, out var window, out var errors))
        {
            throw new McpException(LogTools.FormatValidationErrors(errors));
        }

        var metricWindow = new MetricWindow(window.Value.From, window.Value.To);
        var hydrate = includeAttributes ?? false;
        var series = await reader
            .GetSeriesAsync(key.Value, metricWindow, options.Value.MaxMetricPoints, hydrate, cancellationToken)
            .ConfigureAwait(false);
        if (series is null)
        {
            throw new McpException($"No instrument found for key (resourceHash={resourceHash}, scopeName='{scopeName}', name='{instrumentName}', kind={kind}).");
        }

        var points = new List<MetricPointDto>(series.Points.Count);
        foreach (var point in series.Points)
        {
            points.Add(point.ToDto());
        }

        var instrumentDto = series.Instrument.ToDto(series.Key, series.LifetimePointCount, series.ServiceName, series.ServiceInstanceId);
        return new MetricSeriesDto(instrumentDto, points, series.Truncated);
    }

    [McpServerTool(Name = "list_metric_services", ReadOnly = true, Idempotent = true)]
    [Description("Distinct service.name values across the recorded instruments. Sorted alphabetically.")]
    public static async Task<IReadOnlyList<string>> ListMetricServicesAsync(
        IMetricReader reader,
        CancellationToken cancellationToken)
    {
        var raw = await reader.GetDistinctServiceNamesAsync(cancellationToken).ConfigureAwait(false);
        var names = new SortedSet<string>(raw, StringComparer.Ordinal);
        return [.. names];
    }
}
