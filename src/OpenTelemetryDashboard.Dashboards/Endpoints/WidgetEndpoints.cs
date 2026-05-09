using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.Dashboards.Endpoints;

/// <summary>
/// Minimal API handlers for the widget definitions CRUD. Wiring lives in
/// <see cref="DashboardsEndpointRouteBuilderExtensions.MapWidgets"/>.
/// Re-uses the <see cref="ConcurrencyProblem"/> envelope defined alongside
/// <see cref="DashboardEndpoints"/> — a single 409 contract for the module.
/// </summary>
internal static class WidgetEndpoints
{
    public static async Task<Ok<IReadOnlyList<WidgetDefinitionDto>>>
        GetAllDefinitionsAsync(IWidgetDefinitionStore store, CancellationToken cancellationToken)
    {
        var result = await store.GetAllAsync(cancellationToken);
        var items = new List<WidgetDefinitionDto>(result.Count);
        foreach (var def in result)
        {
            items.Add(ToDto(def));
        }
        return TypedResults.Ok<IReadOnlyList<WidgetDefinitionDto>>(items);
    }

    public static async Task<Results<Ok<WidgetDefinitionDto>, NotFound>>
        GetDefinitionByIdAsync(
            [FromRoute] Guid id,
            IWidgetDefinitionStore store,
            CancellationToken cancellationToken)
    {
        var def = await store.GetByIdAsync(id, cancellationToken);
        if (def is null) return TypedResults.NotFound();
        return TypedResults.Ok(ToDto(def));
    }

    public static async Task<Results<Ok<WidgetDefinitionDto>, ValidationProblem>>
        PostDefinitionAsync(
            [FromBody] SaveWidgetDefinitionRequest request,
            IWidgetDefinitionStore store,
            CancellationToken cancellationToken)
    {
        if (!request.TryValidateRequest(out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var saved = new WidgetDefinition
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
            Engine = request.Engine,
            BaseKind = request.BaseKind,
            ConfigJson = request.Config.GetRawText(),
            SpecJson = request.Spec?.GetRawText(),
            DefaultW = request.DefaultW,
            DefaultH = request.DefaultH,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = 1
        };

        await store.AddAsync(saved, cancellationToken);
        return TypedResults.Ok(ToDto(saved));
    }

    public static async Task<Results<Ok<WidgetDefinitionDto>, ValidationProblem, NotFound, Conflict<ConcurrencyProblem>>>
        PutDefinitionAsync(
            [FromRoute] Guid id,
            [FromBody] SaveWidgetDefinitionRequest request,
            IWidgetDefinitionStore store,
            CancellationToken cancellationToken)
    {
        var existing = await store.GetByIdAsync(id, cancellationToken);
        if (existing is null) return TypedResults.NotFound();

        if (!request.TryValidateRequest(out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var updatedAt = DateTimeOffset.UtcNow;
        var saved = new WidgetDefinition
        {
            Id = id,
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
            Engine = request.Engine,
            BaseKind = request.BaseKind,
            ConfigJson = request.Config.GetRawText(),
            SpecJson = request.Spec?.GetRawText(),
            DefaultW = request.DefaultW,
            DefaultH = request.DefaultH,
            UpdatedAt = updatedAt,
            RowVersion = request.RowVersion
        };

        try
        {
            await store.UpdateAsync(saved, cancellationToken);
        }
        catch (WidgetDefinitionConcurrencyException ex)
        {
            return TypedResults.Conflict(new ConcurrencyProblem(ex.Message));
        }

        // The store bumped RowVersion in place; build the response with the
        // post-save version so the client can use it for the next save.
        var persisted = new WidgetDefinition
        {
            Id = saved.Id,
            Name = saved.Name,
            Description = saved.Description,
            Icon = saved.Icon,
            Engine = saved.Engine,
            BaseKind = saved.BaseKind,
            ConfigJson = saved.ConfigJson,
            SpecJson = saved.SpecJson,
            DefaultW = saved.DefaultW,
            DefaultH = saved.DefaultH,
            UpdatedAt = saved.UpdatedAt,
            RowVersion = checked(request.RowVersion + 1)
        };
        return TypedResults.Ok(ToDto(persisted));
    }

    public static async Task<Results<Ok, NotFound, Conflict<ConcurrencyProblem>>>
        DeleteDefinitionAsync(
            [FromRoute] Guid id,
            [FromQuery] uint rowVersion,
            IWidgetDefinitionStore store,
            CancellationToken cancellationToken)
    {
        var existing = await store.GetByIdAsync(id, cancellationToken);
        if (existing is null) return TypedResults.NotFound();

        // Optimistic concurrency: refuse the delete if the caller's
        // RowVersion doesn't match the loaded one — another writer modified
        // the definition between the SPA's last GET and this DELETE.
        if (existing.RowVersion != rowVersion)
        {
            return TypedResults.Conflict(new ConcurrencyProblem(
                "The widget definition has been modified by another writer. Reload and retry."));
        }

        await store.DeleteAsync(existing, cancellationToken);
        return TypedResults.Ok();
    }

    private static WidgetDefinitionDto ToDto(WidgetDefinition def) => new(
        def.Id,
        def.Name,
        def.Description,
        def.Icon,
        def.Engine,
        def.BaseKind,
        // Stored config text is opaque; round-trip back as JsonElement so
        // clients consume their typed shape. Deserialize<JsonElement> copies
        // the bytes so the element survives the source string going out of
        // scope.
        JsonSerializer.Deserialize<JsonElement>(def.ConfigJson),
        def.SpecJson is null ? null : JsonSerializer.Deserialize<JsonElement>(def.SpecJson),
        def.DefaultW,
        def.DefaultH,
        def.UpdatedAt,
        def.RowVersion);
}
