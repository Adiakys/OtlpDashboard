using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding target for <c>GET /api/v1/metrics/points</c>.
/// The four identity fields (<c>resourceHash</c>, <c>scopeName</c>,
/// <c>instrumentName</c>, <c>kind</c>) together pick a single time-series
/// out of the metric store; <c>from</c>/<c>to</c> narrow the point list
/// to a time window. <c>includeAttributes</c> is opt-in (default
/// <c>false</c>): the per-point attribute map is a JSON-encoded column,
/// so callers that only need the scalar value (Stat, Sparkline, Gauge)
/// skip both the bytes on the wire and the deserialisation cost.
/// </summary>
internal sealed record MetricPointsQueryParameters(
    [FromQuery(Name = "resourceHash")] string? ResourceHash,
    [FromQuery(Name = "scopeName")] string? ScopeName,
    [FromQuery(Name = "instrumentName")] string? InstrumentName,
    [FromQuery(Name = "kind")] string? Kind,
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To,
    [FromQuery(Name = "includeAttributes")] bool? IncludeAttributes = null);

/// <summary>
/// HTTP handlers for the metrics read-side. Wiring lives in
/// <see cref="QueryApiExtensions.MapQueryApi"/>; this class holds only the
/// per-endpoint logic. Backed by <see cref="IMetricReader"/>, persisted in
/// EF Core through <c>EfCoreMetricReader</c>.
/// </summary>
internal static class MetricsEndpoints
{
    public static async Task<Ok<IReadOnlyList<InstrumentDto>>> ListInstrumentsAsync(
        IMetricReader reader,
        CancellationToken cancellationToken)
    {
        var summaries = await reader.ListInstrumentsAsync(cancellationToken).ConfigureAwait(false);
        var items = new List<InstrumentDto>(summaries.Count);
        foreach (var summary in summaries)
        {
            items.Add(summary.Instrument.ToDto(summary.Key, summary.PointCount, summary.ServiceName, summary.ServiceInstanceId));
        }
        return TypedResults.Ok<IReadOnlyList<InstrumentDto>>(items);
    }

    public static async Task<Results<Ok<MetricSeriesDto>, NotFound, ValidationProblem>> GetPointsAsync(
        [AsParameters] MetricPointsQueryParameters parameters,
        IMetricReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryBuildMetricPointsQuery(
                parameters, options.Value, out var key, out var window, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var metricWindow = new MetricWindow(window.Value.From, window.Value.To);
        var includeAttributes = parameters.IncludeAttributes ?? false;
        var series = await reader
            .GetSeriesAsync(key.Value, metricWindow, options.Value.MaxMetricPoints, includeAttributes, cancellationToken)
            .ConfigureAwait(false);
        if (series is null)
        {
            return TypedResults.NotFound();
        }

        var points = new List<MetricPointDto>(series.Points.Count);
        foreach (var point in series.Points)
        {
            points.Add(point.ToDto());
        }

        var instrumentDto = series.Instrument.ToDto(series.Key, series.LifetimePointCount, series.ServiceName, series.ServiceInstanceId);
        return TypedResults.Ok(new MetricSeriesDto(instrumentDto, points, series.Truncated));
    }
}
