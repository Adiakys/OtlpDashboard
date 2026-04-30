using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
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
    /// <summary>
    /// Library ids must match the same shape the manifest parser enforces.
    /// Validating here turns "/api/v1/widgets/libraries/../etc/passwd" into
    /// a 400 long before the registry sees it.
    /// </summary>
    private static readonly Regex LibraryIdRegex = new(
        @"^[a-z0-9](-?[a-z0-9])*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public static async Task<Results<Created<WidgetLibraryDto>, ValidationProblem, BadRequest<ProblemDetails>, Conflict<ProblemDetails>, UnprocessableEntity<ProblemDetails>>>
        InstallLibraryAsync(
            [FromBody] InstallLibraryRequest request,
            IWidgetLibraryInstaller installer,
            CancellationToken cancellationToken)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Url)
            || string.IsNullOrWhiteSpace(request.Ref))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["body"] = ["The 'url' and 'ref' fields are required."]
            });
        }

        try
        {
            var lib = await installer.InstallAsync(request.Url, request.Ref, cancellationToken);
            return TypedResults.Created($"/api/v1/widgets/libraries/{lib.Id}", ToDto(lib));
        }
        catch (WidgetLibraryHostNotAllowedException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Host not allowed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (WidgetLibraryIdCollisionException ex)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Library id collision",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (WidgetLibraryManifestInvalidException ex)
        {
            return TypedResults.UnprocessableEntity(new ProblemDetails
            {
                Title = "manifest.json invalid",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }

    public static async Task<Results<Ok<WidgetLibraryDto>, ValidationProblem, NotFound, BadRequest<ProblemDetails>>>
        UpdateLibraryAsync(
            [FromRoute] string id,
            IWidgetLibraryInstaller installer,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !LibraryIdRegex.IsMatch(id))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["id"] = ["Library id must be lowercase alphanumeric with optional hyphens (max 64 chars)."]
            });
        }

        try
        {
            var lib = await installer.UpdateAsync(id, cancellationToken);
            return TypedResults.Ok(ToDto(lib));
        }
        catch (WidgetLibraryNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (WidgetLibraryNotGitInstalledException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Library not git-installed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, BadRequest<ProblemDetails>>>
        UninstallLibraryAsync(
            [FromRoute] string id,
            IWidgetLibraryRegistry registry,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || !LibraryIdRegex.IsMatch(id))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["id"] = ["Library id must be lowercase alphanumeric with optional hyphens (max 64 chars)."]
            });
        }

        try
        {
            await registry.UninstallAsync(id, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (WidgetLibraryNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (WidgetLibraryNotRemovableException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Library not removable",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
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
            lib.Removable,
            widgets);
    }
}
