using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Api.Mappings;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Metrics;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding target for <c>GET /api/v1/metrics/points</c>.
/// The four identity fields (<c>resourceHash</c>, <c>scopeName</c>,
/// <c>instrumentName</c>, <c>kind</c>) together pick a single time-series
/// out of the in-memory store; <c>from</c>/<c>to</c> narrow the point list
/// to a time window.
/// </summary>
internal sealed record MetricPointsQueryParameters(
    [FromQuery(Name = "resourceHash")] string? ResourceHash,
    [FromQuery(Name = "scopeName")] string? ScopeName,
    [FromQuery(Name = "instrumentName")] string? InstrumentName,
    [FromQuery(Name = "kind")] string? Kind,
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To);

/// <summary>
/// HTTP handlers for the metrics read-side. Wiring lives in
/// <see cref="QueryApiExtensions.MapQueryApi"/>; this class holds only the
/// per-endpoint logic. Backed by <see cref="IMetricReader"/>, which today
/// is an in-memory ring-buffer store shared with the OTLP ingestion pipeline.
/// </summary>
internal static class MetricsEndpoints
{
    public static Ok<IReadOnlyList<InstrumentDto>> ListInstruments(IMetricReader reader)
    {
        var keys = reader.GetInstrumentKeys();
        var items = new List<InstrumentDto>(keys.Count);

        foreach (var key in keys)
        {
            var instrument = reader.GetInstrument(key);
            if (instrument is null)
            {
                // The key was evicted between enumeration and lookup; skip.
                continue;
            }

            var pointCount = reader.GetPoints(key).Count;
            var serviceName = reader.GetServiceName(key);
            items.Add(instrument.ToDto(key, pointCount, serviceName));
        }

        // Deterministic ordering: (ScopeName, Name, Kind). Makes the UI
        // stable across refreshes when the set of instruments is constant.
        items.Sort(static (a, b) =>
        {
            var byScope = string.CompareOrdinal(a.ScopeName, b.ScopeName);
            if (byScope != 0) return byScope;
            var byName = string.CompareOrdinal(a.Name, b.Name);
            if (byName != 0) return byName;
            return string.CompareOrdinal(a.Kind, b.Kind);
        });

        return TypedResults.Ok<IReadOnlyList<InstrumentDto>>(items);
    }

    public static Results<Ok<MetricSeriesDto>, NotFound, ValidationProblem> GetPoints(
        [AsParameters] MetricPointsQueryParameters parameters,
        IMetricReader reader,
        IOptions<QueryApiOptions> options)
    {
        if (!QueryValidation.TryBuildMetricPointsQuery(
                parameters, options.Value, out var key, out var window, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var instrument = reader.GetInstrument(key.Value);
        if (instrument is null)
        {
            return TypedResults.NotFound();
        }

        var snapshot = reader.GetPoints(key.Value);

        IReadOnlyList<DataPoint> filtered;
        if (window is { } w)
        {
            var fromNano = UnixNanoTime.ToUnixNanoseconds(w.From);
            var toNano = UnixNanoTime.ToUnixNanoseconds(w.To);
            var buffer = new List<DataPoint>(snapshot.Count);
            foreach (var point in snapshot)
            {
                if (point.TimeUnixNano >= fromNano && point.TimeUnixNano < toNano)
                {
                    buffer.Add(point);
                }
            }
            filtered = buffer;
        }
        else
        {
            filtered = snapshot;
        }

        var points = new List<MetricPointDto>(filtered.Count);
        foreach (var point in filtered)
        {
            points.Add(point.ToDto());
        }

        var instrumentDto = instrument.ToDto(key.Value, snapshot.Count, reader.GetServiceName(key.Value));
        return TypedResults.Ok(new MetricSeriesDto(instrumentDto, points));
    }
}
