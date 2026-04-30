using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.Dashboards.Endpoints;

/// <summary>
/// Read-only endpoints exposing widget libraries discovered on disk.
/// Wiring lives in
/// <see cref="DashboardsEndpointRouteBuilderExtensions.MapWidgets"/>.
/// </summary>
internal static class LibraryEndpoints
{
    public static async Task<Ok<IReadOnlyList<WidgetLibraryDto>>>
        GetLibrariesAsync(IWidgetLibraryRegistry registry, CancellationToken cancellationToken)
    {
        var libraries = await registry.ListAsync(cancellationToken);
        var dtos = new List<WidgetLibraryDto>(libraries.Count);
        foreach (var lib in libraries)
        {
            dtos.Add(ToDto(lib));
        }
        return TypedResults.Ok<IReadOnlyList<WidgetLibraryDto>>(dtos);
    }

    public static async Task<NoContent>
        ReloadLibrariesAsync(IWidgetLibraryRegistry registry, CancellationToken cancellationToken)
    {
        await registry.ReloadAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static WidgetLibraryDto ToDto(WidgetLibrary lib)
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
                w.DefaultW,
                w.DefaultH));
        }

        return new WidgetLibraryDto(
            lib.Id,
            lib.Name,
            lib.Version,
            lib.Author,
            lib.License,
            lib.Description,
            lib.InstallSource,
            lib.GitUrl,
            lib.GitRef,
            lib.GitRefResolved,
            lib.InstalledAt,
            widgets);
    }
}
