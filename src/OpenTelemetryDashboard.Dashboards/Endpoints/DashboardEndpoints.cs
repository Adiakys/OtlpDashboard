using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;
using OpenTelemetryDashboard.Dashboards.Validation;

namespace OpenTelemetryDashboard.Dashboards.Endpoints;

/// <summary>
/// Minimal API handlers for the default dashboard. Wiring lives in
/// <see cref="DashboardsEndpointRouteBuilderExtensions.MapDashboards"/>.
/// </summary>
internal static class DashboardEndpoints
{
    public static async Task<Ok<DashboardDto>> GetDefaultAsync(
        IDashboardStore store,
        CancellationToken cancellationToken)
    {
        var dashboard = await store.GetDefaultAsync(cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(ToDto(dashboard));
    }

    public static async Task<Results<Ok<DashboardDto>, ValidationProblem, Conflict<ConcurrencyProblem>>> SaveDefaultAsync(
        SaveDashboardRequest request,
        IDashboardStore store,
        CancellationToken cancellationToken)
    {
        if (!DashboardValidation.TryValidateSave(request, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            var saved = await store
                .SaveDefaultAsync(request.Name, request.LayoutJson, request.RowVersion, cancellationToken)
                .ConfigureAwait(false);
            return TypedResults.Ok(ToDto(saved));
        }
        catch (DashboardConcurrencyException ex)
        {
            return TypedResults.Conflict(new ConcurrencyProblem(ex.Message));
        }
    }

    private static DashboardDto ToDto(Dashboard dashboard) => new(
        dashboard.Id,
        dashboard.Name,
        dashboard.LayoutJson,
        dashboard.UpdatedAt,
        dashboard.RowVersion);
}

/// <summary>
/// Wire shape for a 409 response. Kept tiny on purpose — the client only
/// needs a human-readable message; the row version it should retry against
/// is fetched via a fresh <c>GET</c>.
/// </summary>
public sealed record ConcurrencyProblem(string Message);
