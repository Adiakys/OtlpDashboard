using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Queries;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding target for <c>GET /api/v1/service-map</c>.
/// </summary>
internal sealed record ServiceMapParameters(
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To,
    [FromQuery(Name = "service")] string? Service = null);

/// <summary>
/// HTTP handlers for the service-map endpoint. Lives at the top level
/// (peer of <see cref="TracesEndpoints"/>, <see cref="LogsEndpoints"/>,
/// <see cref="MetricsEndpoints"/>) because the service-map view is its
/// own pillar — that the implementation happens to query span storage
/// is a detail of <see cref="IServiceMapReader"/>.
/// </summary>
internal static class ServiceMapEndpoints
{
    public static async Task<Results<Ok<ServiceMapDto>, ValidationProblem>> GetServiceMapAsync(
        [AsParameters] ServiceMapParameters parameters,
        IServiceMapReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryBuildServiceMapQuery(parameters, options.Value, out var query, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await reader.GetServiceMapAsync(query, cancellationToken).ConfigureAwait(false);
        var nodes = result.Nodes
            .Select(n => new ServiceMapNodeDto(
                n.Service,
                n.Kind == ServiceMapNodeKind.Dependency ? "dependency" : "service",
                n.RequestCount,
                n.ErrorCount))
            .ToList();
        var edges = result.Edges
            .Select(e => new ServiceMapEdgeDto(e.FromService, e.ToService, e.CallCount, e.ErrorCount))
            .ToList();
        return TypedResults.Ok(new ServiceMapDto(nodes, edges));
    }
}
