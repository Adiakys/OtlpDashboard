using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.Dashboards.Endpoints;

/// <summary>
/// Read-only picker contract: a flat list of widget libraries
/// surfaced from every installed pack. Pack management (install /
/// update / uninstall) lives in <see cref="PackEndpoints"/>; this
/// file exists so the SPA's widget picker has a stable URL that
/// doesn't need to know about packs at all.
/// </summary>
internal static class WidgetLibraryEndpoints
{
    public static async Task<Ok<IReadOnlyList<WidgetLibraryDto>>>
        GetLibrariesAsync(IWidgetLibraryRegistry registry, CancellationToken cancellationToken)
    {
        var libraries = await registry.ListAsync(cancellationToken);
        var dtos = new List<WidgetLibraryDto>(libraries.Count);
        foreach (var lib in libraries) dtos.Add(ToDto(lib));
        return TypedResults.Ok<IReadOnlyList<WidgetLibraryDto>>(dtos);
    }

    internal static WidgetLibraryDto ToDto(WidgetLibrary lib)
    {
        var widgets = new List<LibraryWidgetDto>(lib.Widgets.Count);
        foreach (var w in lib.Widgets)
        {
            widgets.Add(new LibraryWidgetDto(
                w.KindId,
                w.Name,
                w.Description,
                w.Icon,
                w.Engine,
                w.BaseKind,
                w.ConfigJson is null ? null : JsonSerializer.Deserialize<JsonElement>(w.ConfigJson),
                w.SpecJson is null ? null : JsonSerializer.Deserialize<JsonElement>(w.SpecJson),
                w.ParametersJson is null ? null : JsonSerializer.Deserialize<JsonElement>(w.ParametersJson),
                w.DefaultW,
                w.DefaultH));
        }

        return new WidgetLibraryDto(
            lib.Id,
            lib.Name,
            lib.Description,
            lib.Icon,
            lib.PackId,
            widgets);
    }
}
