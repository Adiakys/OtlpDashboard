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

    /// <summary>
    /// Asset extensions the pack asset endpoint will serve. Mirrors
    /// <c>LibraryManifestParser.IsSafeIconImageFilename</c>; both sides
    /// must agree so a manifest-validated path can never miss the
    /// runtime guard.
    /// </summary>
    private static readonly HashSet<string> AllowedAssetExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".svg", ".png", ".webp" };

    private static readonly Dictionary<string, string> AssetContentTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".svg"] = "image/svg+xml",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
        };

    /// <summary>
    /// Serves a binary asset from a pack's directory. Scoped to a fixed
    /// extension whitelist so the endpoint can't be repurposed to read
    /// arbitrary files (config snippets, install metadata) out of the
    /// pack root, and double-checks containment by absolute-path
    /// comparison after the OS resolves any symlinks.
    /// </summary>
    public static async Task<Results<FileContentHttpResult, NotFound, BadRequest<ProblemDetails>>>
        GetPackAssetAsync(
            [FromRoute] string id,
            HttpContext httpContext,
            IPackRegistry registry,
            CancellationToken cancellationToken)
    {
        if (!IsValidId(id))
        {
            return TypedResults.BadRequest(new ProblemDetails
            {
                Title = "Invalid pack id",
                Detail = "Pack id must be lowercase alphanumeric with optional hyphens (max 64 chars).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Catch-all parameters are bound from RouteValues — we read them
        // here because the [FromRoute] attribute can't reliably bind a
        // route segment containing slashes across versions of ASP.NET.
        var rawPath = httpContext.Request.RouteValues["path"] as string;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return TypedResults.NotFound();
        }

        // Reject obvious traversal attempts before touching the
        // filesystem. Defence in depth: GetFullPath would resolve them
        // anyway, but this gives a cheap fast-fail and a clear log line.
        if (rawPath.Contains("..", StringComparison.Ordinal)
            || rawPath.Contains('\\', StringComparison.Ordinal)
            || rawPath.StartsWith('/'))
        {
            return TypedResults.NotFound();
        }

        var ext = Path.GetExtension(rawPath);
        if (!AllowedAssetExtensions.Contains(ext))
        {
            return TypedResults.NotFound();
        }

        var packs = await registry.ListAsync(cancellationToken);
        var pack = packs.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (pack is null) return TypedResults.NotFound();

        var rootPath = pack.RootPath;
        if (string.IsNullOrEmpty(rootPath)) return TypedResults.NotFound();

        var fullPath = Path.GetFullPath(Path.Combine(rootPath, rawPath));
        var rootWithSep = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            return TypedResults.NotFound();
        }

        if (!File.Exists(fullPath))
        {
            return TypedResults.NotFound();
        }

        // Symlink guard. Path.GetFullPath only canonicalises the string
        // (it doesn't resolve link targets), so a pack that contained
        // `icons/evil.svg -> /etc/passwd` would pass the StartsWith
        // containment check and exfiltrate the target on read. Refusing
        // to follow any reparse point keeps the asset surface aligned
        // with the registry's existing top-level symlink ban.
        var fileInfo = new FileInfo(fullPath);
        if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return TypedResults.NotFound();
        }
        // Walk parent directories: an intermediate symlink (e.g.
        // `icons/postgres -> /var/something`) lets the leaf file land
        // outside the pack even when the leaf itself is a regular file.
        for (var dir = fileInfo.Directory; dir is not null; dir = dir.Parent)
        {
            if (string.Equals(dir.FullName, rootPath, StringComparison.Ordinal)) break;
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return TypedResults.NotFound();
            }
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }
        catch (IOException)
        {
            return TypedResults.NotFound();
        }

        // Defence-in-depth on top of the CSP override below: parse the SVG
        // server-side and drop active content (<script>, <foreignObject>,
        // SMIL animations, on* event handlers, javascript: URIs in href).
        // A malformed SVG returns 404 rather than a passthrough — the asset
        // surface refuses to serve bytes it can't audit.
        if (string.Equals(ext, ".svg", StringComparison.OrdinalIgnoreCase))
        {
            var sanitized = SvgSanitizer.TrySanitize(bytes);
            if (sanitized is null) return TypedResults.NotFound();
            bytes = sanitized;
        }

        var contentType = AssetContentTypes.TryGetValue(ext, out var ct)
            ? ct
            : "application/octet-stream";

        // ETag = pack version + relative path. Stable across processes,
        // bumps on pack update.
        var etag = $"\"{pack.Version}-{rawPath.GetHashCode(StringComparison.Ordinal):x8}\"";
        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers.CacheControl = "public, max-age=300";

        // Asset hardening. The endpoint is anonymous and serves SVG/PNG/WebP
        // from disk; even with the extension whitelist + traversal/symlink
        // guards plus SvgSanitizer, an SVG with inline content would
        // otherwise have multiple chances to execute JS in this origin.
        // Override the SPA-shell CSP with the strictest policy compatible
        // with rendering an image, force the browser to honour the declared
        // Content-Type instead of MIME-sniffing, and pin Content-Disposition
        // to inline so a download flow can't get a quirks-mode interpretation.
        httpContext.Response.Headers["Content-Security-Policy"] =
            "default-src 'none'; style-src 'unsafe-inline'; sandbox;";
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        httpContext.Response.Headers["Content-Disposition"] =
            $"inline; filename=\"{SafeContentDispositionFilename(rawPath)}\"";

        return TypedResults.File(bytes, contentType: contentType);
    }

    /// <summary>
    /// Strips path separators and quotes from the raw asset path so the
    /// value is safe inside a <c>Content-Disposition</c> filename token.
    /// The path has already been validated against traversal/whitelist
    /// rules above, but the header sits in the response without any
    /// further escaping — better to keep it simple.
    /// </summary>
    private static string SafeContentDispositionFilename(string rawPath)
    {
        var name = Path.GetFileName(rawPath);
        if (string.IsNullOrEmpty(name)) return "asset";
        var buf = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            buf[i] = c is '"' or '\\' or '/' or '\r' or '\n' || c < 0x20 ? '_' : c;
        }
        return new string(buf);
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

        var icons = new List<PackIconDto>(pack.Icons.Count);
        foreach (var icon in pack.Icons)
        {
            var match = new List<PackIconMatchDto>(icon.Match.Count);
            foreach (var m in icon.Match)
            {
                match.Add(new PackIconMatchDto(m.ServiceName, m.NamePattern));
            }
            icons.Add(new PackIconDto(
                icon.Id,
                icon.Name,
                $"/api/v1/packs/{pack.Id}/assets/{icon.Image}",
                match));
        }

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
            dashboards,
            icons);
    }
}
