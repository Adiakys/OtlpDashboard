using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.Dashboards.Endpoints;

/// <summary>
/// Minimal API handlers for the dashboards CRUD. Wiring lives in
/// <see cref="DashboardsEndpointRouteBuilderExtensions.MapDashboards"/>.
/// </summary>
internal static class DashboardEndpoints
{
    public static async Task<Ok<IEnumerable<DashboardDto>>>
        GetAllDashboardAsync(IDashboardStore store, CancellationToken cancellationToken)
    {
        var result = await store.GetAllAsync(cancellationToken);
        return TypedResults.Ok(result.Select(ToDto));
    }

    public static async Task<Results<Ok<DashboardDto>, NotFound>>
        GetDashboardByIdAsync([FromRoute] Guid id, IDashboardStore store, CancellationToken cancellationToken)
    {
        var dashboard = await store.GetByIdAsync(id, cancellationToken);

        if (dashboard is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToDto(dashboard));
    }

    public static async Task<Results<Ok<DashboardDto>, ValidationProblem>>
        PostDashboardAsync([FromBody] SaveDashboardRequest request, IDashboardStore store, CancellationToken cancellationToken)
    {
        if (!request.TryValidateRequest(out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var dashboardId = Guid.CreateVersion7();
        var saved = new Dashboard
        {
            Id = dashboardId,
            Name = request.Name,
            Widgets = request.Widgets.Select(w => ToDomain(w, dashboardId)).ToList(),
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = 1
        };

        await store.AddAsync(saved, cancellationToken);
        return TypedResults.Ok(ToDto(saved));
    }

    public static async Task<Results<Ok<DashboardDto>, ValidationProblem, NotFound, Conflict<ConcurrencyProblem>>>
        PutDashboardAsync([FromRoute] Guid id, [FromBody] SaveDashboardRequest request, IDashboardStore store, CancellationToken cancellationToken)
    {
        var existing = await store.GetByIdAsync(id, cancellationToken);

        if (existing is null) return TypedResults.NotFound();

        if (!request.TryValidateRequest(out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Pass the client's claimed RowVersion as-is. The store loads the
        // row, verifies the match, increments, and saves — so any drift
        // between this load and the store's load surfaces as a concurrency
        // conflict.
        var updatedAt = DateTimeOffset.UtcNow;
        var saved = new Dashboard
        {
            Id = id,
            Name = request.Name,
            Widgets = request.Widgets.Select(w => ToDomain(w, id)).ToList(),
            UpdatedAt = updatedAt,
            RowVersion = request.RowVersion
        };

        try
        {
            await store.UpdateAsync(saved, cancellationToken);
        }
        catch (DashboardConcurrencyException ex)
        {
            return TypedResults.Conflict(new ConcurrencyProblem(ex.Message));
        }

        // The store bumped RowVersion internally; build the response with
        // the post-save version so the client can use it as the basis for
        // the next save.
        var persisted = new Dashboard
        {
            Id = saved.Id,
            Name = saved.Name,
            Widgets = saved.Widgets,
            UpdatedAt = updatedAt,
            RowVersion = checked(request.RowVersion + 1)
        };
        return TypedResults.Ok(ToDto(persisted));
    }

    public static async Task<Results<Ok, NotFound, ValidationProblem>>
        DeleteDashboardAsync([FromRoute] Guid id, IDashboardStore store, CancellationToken cancellationToken)
    {
        if (id == Dashboard.DefaultId)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["id"] = ["The default dashboard cannot be deleted."]
            });
        }

        var existing = await store.GetByIdAsync(id, cancellationToken);

        if (existing is null) return TypedResults.NotFound();

        await store.DeleteAsync(existing, cancellationToken);

        return TypedResults.Ok();
    }

    private static DashboardDto ToDto(Dashboard dashboard) => new(
        dashboard.Id,
        dashboard.Name,
        dashboard.Widgets.Select(ToDto).ToList(),
        dashboard.UpdatedAt,
        dashboard.RowVersion);

    private static DashboardWidgetDto ToDto(DashboardWidget widget) => new(
        widget.Id,
        widget.Kind,
        widget.X,
        widget.Y,
        widget.W,
        widget.H,
        // Stored config text is opaque to the server; round-trip it back as a
        // structured JsonElement so clients consume their own typed shape.
        // Deserialize<JsonElement> copies the bytes, so the element stays
        // valid after the source string is gone.
        JsonSerializer.Deserialize<JsonElement>(widget.ConfigJson));

    private static DashboardWidget ToDomain(DashboardWidgetDto dto, Guid dashboardId) => new()
    {
        // Empty Guid means "client didn't pre-allocate one" — common on first
        // create. Generate a v7 so widgets keep a stable id across saves.
        Id = dto.Id == Guid.Empty ? Guid.CreateVersion7() : dto.Id,
        DashboardId = dashboardId,
        Kind = dto.Kind,
        X = dto.X,
        Y = dto.Y,
        W = dto.W,
        H = dto.H,
        ConfigJson = dto.Config.GetRawText()
    };
}

/// <summary>
/// Wire shape for a 409 response. Kept tiny on purpose — the client only
/// needs a human-readable message; the row version it should retry against
/// is fetched via a fresh <c>GET</c>.
/// </summary>
public sealed record ConcurrencyProblem(string Message);
