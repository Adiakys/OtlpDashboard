using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.Dashboards.Endpoints;

/// <summary>
/// Endpoints for the pack management surface — install / update /
/// uninstall / list. The read-side picker contract for libraries
/// continues to live in <see cref="WidgetLibraryEndpoints"/>; this
/// file owns the pack-as-a-unit operations. Wiring lives in
/// <see cref="DashboardsEndpointRouteBuilderExtensions.MapPacks"/>.
/// </summary>
internal static class PackEndpoints
{
    /// <summary>
    /// Pack ids share the same shape as library ids. Validating here
    /// turns "/api/v1/packs/../etc/passwd" into a 400 long before the
    /// registry sees it.
    /// </summary>
    private static readonly Regex PackIdRegex = new(
        @"^[a-z0-9](-?[a-z0-9])*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<Ok<IReadOnlyList<PackDto>>>
        GetPacksAsync(IPackRegistry registry, CancellationToken cancellationToken)
    {
        var packs = await registry.ListAsync(cancellationToken);
        var dtos = new List<PackDto>(packs.Count);
        foreach (var p in packs) dtos.Add(ToDto(p));
        return TypedResults.Ok<IReadOnlyList<PackDto>>(dtos);
    }

    public static async Task<NoContent>
        ReloadPacksAsync(IPackRegistry registry, CancellationToken cancellationToken)
    {
        await registry.ReloadAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    public static async Task<Results<Created<PackDto>, ValidationProblem, BadRequest<ProblemDetails>, Conflict<ProblemDetails>, UnprocessableEntity<ProblemDetails>>>
        InstallPackAsync(
            [FromBody] InstallPackRequest request,
            IPackInstaller installer,
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
            var pack = await installer.InstallAsync(request.Url, request.Ref, request.Path, cancellationToken);
            return TypedResults.Created($"/api/v1/packs/{pack.Id}", ToDto(pack));
        }
        catch (PackHostNotAllowedException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Host not allowed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (PackInstallPathInvalidException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Install path invalid",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (PackIdCollisionException ex)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Title = "Pack id collision",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (PackManifestInvalidException ex)
        {
            return TypedResults.UnprocessableEntity(new ProblemDetails
            {
                Title = "pack.json invalid",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
    }

    public static async Task<Results<Ok<PackDto>, ValidationProblem, NotFound, BadRequest<ProblemDetails>>>
        UpdatePackAsync(
            [FromRoute] string id,
            IPackInstaller installer,
            CancellationToken cancellationToken)
    {
        if (!IsValidId(id))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["id"] = ["Pack id must be lowercase alphanumeric with optional hyphens (max 64 chars)."]
            });
        }

        try
        {
            var pack = await installer.UpdateAsync(id, cancellationToken);
            return TypedResults.Ok(ToDto(pack));
        }
        catch (PackNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (PackNotGitInstalledException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Pack not git-installed",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    public static async Task<Results<NoContent, NotFound, ValidationProblem, BadRequest<ProblemDetails>>>
        UninstallPackAsync(
            [FromRoute] string id,
            IPackRegistry registry,
            CancellationToken cancellationToken)
    {
        if (!IsValidId(id))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["id"] = ["Pack id must be lowercase alphanumeric with optional hyphens (max 64 chars)."]
            });
        }

        try
        {
            await registry.UninstallAsync(id, cancellationToken);
            return TypedResults.NoContent();
        }
        catch (PackNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (PackNotRemovableException ex)
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Pack not removable",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && PackIdRegex.IsMatch(id);

    internal static PackDto ToDto(Pack pack)
    {
        var libraries = new List<WidgetLibraryDto>(pack.Libraries.Count);
        foreach (var lib in pack.Libraries) libraries.Add(WidgetLibraryEndpoints.ToDto(lib));

        var dashboards = new List<PackDashboardDto>(pack.Dashboards.Count);
        foreach (var d in pack.Dashboards) dashboards.Add(new PackDashboardDto(d.Id, d.Builtin));

        return new PackDto(
            pack.Id,
            pack.Name,
            pack.Version,
            pack.Author,
            pack.License,
            pack.Description,
            pack.Homepage,
            pack.InstallSource,
            pack.GitUrl,
            pack.GitRef,
            pack.GitRefResolved,
            pack.GitSubPath,
            pack.InstalledAt,
            pack.Removable,
            libraries,
            dashboards);
    }
}
