using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Orchestrator for installing and updating packs from git. Drives
/// <see cref="IGitInstaller"/> through the transactional pipeline
/// (clone-tmp → optional sub-path re-root → parse pack.json → write
/// .install.json → atomic move) and invalidates the registry cache
/// after each successful operation.
/// </summary>
public sealed partial class PackInstallService : IPackInstaller
{
    /// <summary>
    /// Schema version stamped into <c>.install.json</c>. Bump when the
    /// shape changes — load-side code that reads the file can branch
    /// on it.
    /// </summary>
    public const int InstallMetadataVersion = 1;

    private static readonly Regex IdRegex = new(
        @"^[a-z0-9](-?[a-z0-9])*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions InstallMetadataJson = new() { WriteIndented = true };

    private readonly IGitInstaller _git;
    private readonly IPackRegistry _registry;
    private readonly PacksOptions _options;
    private readonly string _primaryRoot;
    private readonly ILogger<PackInstallService> _logger;

    public PackInstallService(
        IGitInstaller git,
        IPackRegistry registry,
        IOptions<PacksOptions> options,
        FilesystemPackRegistry primaryPathProvider,
        ILogger<PackInstallService> logger)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(primaryPathProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _git = git;
        _registry = registry;
        _options = options.Value;
        _primaryRoot = primaryPathProvider.PacksPaths.Count > 0
            ? primaryPathProvider.PacksPaths[0]
            : throw new InvalidOperationException("No packs path is configured.");
        _logger = logger;
    }

    public async Task<Pack> InstallAsync(
        string url,
        string gitRef,
        string? subPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRef);

        ValidateUrl(url);
        var normalizedSubPath = NormalizeSubPath(subPath);

        Directory.CreateDirectory(_primaryRoot);

        // Clone into a hidden tmp directory so a failure mid-pipeline
        // doesn't surface a half-installed pack to the registry's next
        // scan. The leading dot keeps it below alphabetical sort and
        // the random suffix is enough on a single-tenant install.
        var tmpDir = Path.Combine(_primaryRoot, $".tmp-{Guid.NewGuid():N}");

        try
        {
            await _git.CloneAsync(url, gitRef, tmpDir,
                TimeSpan.FromSeconds(_options.GitInstallTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeDelete(tmpDir);
            throw new PackGitOperationException("Clone failed.", ex);
        }

        try
        {
            // Re-root the pack on the optional sub-path; the parser then
            // validates pack.json exists and matches the on-disk dir
            // name.
            var packRoot = ResolvePackRoot(tmpDir, normalizedSubPath);
            var packPath = Path.Combine(packRoot, "pack.json");
            if (!File.Exists(packPath))
            {
                throw new PackManifestInvalidException("pack.json is missing at the configured path.");
            }

            // Sniff the id first (we don't yet know the canonical name);
            // then strict parse against the matching expected id.
            var raw = await File.ReadAllTextAsync(packPath, cancellationToken).ConfigureAwait(false);
            var sniffedId = SniffId(raw);
            if (sniffedId is null || !IdRegex.IsMatch(sniffedId))
            {
                throw new PackManifestInvalidException("pack.json: 'id' is missing or invalid.");
            }
            if (!LibraryManifestParser.TryParsePack(raw, sniffedId, out _, out var parseError))
            {
                throw new PackManifestInvalidException(parseError ?? "pack manifest validation failed.");
            }

            var targetDir = Path.Combine(_primaryRoot, sniffedId);
            if (Directory.Exists(targetDir))
            {
                throw new PackIdCollisionException(sniffedId);
            }

            // Stage: when the pack lives in a sub-path, move only that
            // sub-tree out of the tmp clone. The `targetDir` ends up
            // being exactly the pack root, never the parent repo.
            var stagedDir = packRoot == tmpDir ? tmpDir : await MoveSubTreeAsync(packRoot, tmpDir, cancellationToken);

            var resolvedSha = _git.ResolveHead(tmpDir);
            var installedAt = DateTimeOffset.UtcNow;
            await WriteInstallMetadataAsync(stagedDir, url, gitRef, resolvedSha, normalizedSubPath, installedAt, cancellationToken)
                .ConfigureAwait(false);

            Directory.Move(stagedDir, targetDir);
            tmpDir = null!; // ownership transferred to the registry

            await _registry.ReloadAsync(cancellationToken).ConfigureAwait(false);
            _logger.PackInstalled(sniffedId, url, gitRef, resolvedSha);

            var packs = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);
            return packs.First(p => p.Id == sniffedId);
        }
        catch (PackInstallException)
        {
            SafeDelete(tmpDir);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tmpDir);
            throw new PackGitOperationException("Install pipeline failed.", ex);
        }
    }

    public async Task<Pack> UpdateAsync(string packId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        if (!IdRegex.IsMatch(packId))
        {
            throw new PackNotFoundException(packId);
        }

        var packDir = Path.Combine(_primaryRoot, packId);
        if (!Directory.Exists(packDir))
        {
            throw new PackNotFoundException(packId);
        }

        var metaPath = Path.Combine(packDir, ".install.json");
        if (!File.Exists(metaPath))
        {
            throw new PackNotGitInstalledException(packId);
        }

        var meta = await ReadInstallMetadataAsync(metaPath, cancellationToken).ConfigureAwait(false);
        if (meta is null || !string.Equals(meta.Source, "git", StringComparison.OrdinalIgnoreCase))
        {
            throw new PackNotGitInstalledException(packId);
        }
        if (string.IsNullOrWhiteSpace(meta.Url) || string.IsNullOrWhiteSpace(meta.Ref))
        {
            throw new PackNotGitInstalledException(packId);
        }

        try
        {
            await _git.FetchAndResetAsync(packDir, meta.Ref,
                TimeSpan.FromSeconds(_options.GitInstallTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PackGitOperationException("Update failed.", ex);
        }

        var resolvedSha = _git.ResolveHead(packDir);
        var installedAt = DateTimeOffset.UtcNow;
        await WriteInstallMetadataAsync(packDir, meta.Url, meta.Ref, resolvedSha, meta.SubPath, installedAt, cancellationToken)
            .ConfigureAwait(false);

        await _registry.ReloadAsync(cancellationToken).ConfigureAwait(false);
        _logger.PackUpdated(packId, meta.Ref, resolvedSha);

        var packs = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);
        return packs.First(p => p.Id == packId);
    }

    private static async Task<string> MoveSubTreeAsync(string packRoot, string tmpRoot, CancellationToken cancellationToken)
    {
        // Copy the sub-tree out of the clone into a sibling directory
        // (so the eventual `Directory.Move` to the primary root remains
        // atomic on any filesystem). Then nuke the rest of the tmp
        // clone — there's nothing in there we need.
        var sibling = Path.Combine(Path.GetDirectoryName(tmpRoot)!, $".tmp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sibling);
        await CopyDirectoryAsync(packRoot, sibling, cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort: a leaked tmp clone wastes disk but the
            // caller already has the staged dir.
        }
        return sibling;
    }

    private static async Task CopyDirectoryAsync(string src, string dst, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, file);
            await using var input = File.OpenRead(file);
            await using var output = File.Create(Path.Combine(dst, rel));
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Validate the optional install sub-path and return its
    /// normalized form (forward slashes, no leading/trailing separators)
    /// or <c>null</c> if the caller didn't pass one.
    /// </summary>
    private static string? NormalizeSubPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim().Replace('\\', '/').Trim('/');
        if (trimmed.Length == 0) return null;
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            throw new PackInstallPathInvalidException("path must not contain a scheme or drive letter.");
        }
        foreach (var segment in trimmed.Split('/'))
        {
            if (segment.Length == 0)
            {
                throw new PackInstallPathInvalidException("path must not contain empty segments.");
            }
            if (segment is "." or "..")
            {
                throw new PackInstallPathInvalidException("path must not traverse with '.' or '..'.");
            }
        }
        return trimmed;
    }

    private static string ResolvePackRoot(string tmpRoot, string? subPath)
    {
        if (string.IsNullOrEmpty(subPath)) return tmpRoot;

        var resolved = Path.GetFullPath(Path.Combine(tmpRoot, subPath));
        var tmpRootFull = Path.GetFullPath(tmpRoot);
        var withSep = tmpRootFull.EndsWith(Path.DirectorySeparatorChar)
            ? tmpRootFull
            : tmpRootFull + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(withSep, StringComparison.Ordinal))
        {
            throw new PackInstallPathInvalidException("path resolves outside the cloned repository.");
        }
        if (!Directory.Exists(resolved))
        {
            throw new PackInstallPathInvalidException($"sub-directory '{subPath}' does not exist in the cloned repository.");
        }
        return resolved;
    }

    private void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            throw new PackHostNotAllowedException(url);
        }
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new PackHostNotAllowedException(parsed.Scheme);
        }
        var host = parsed.Host.ToLowerInvariant();
        if (!_options.AllowedGitHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new PackHostNotAllowedException(host);
        }
    }

    private static string? SniffId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return doc.RootElement.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteInstallMetadataAsync(
        string dir,
        string url,
        string gitRef,
        string refResolved,
        string? subPath,
        DateTimeOffset installedAt,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(dir, ".install.json");
        var payload = new Dictionary<string, object?>
        {
            ["version"] = InstallMetadataVersion,
            ["source"] = "git",
            ["url"] = url,
            ["ref"] = gitRef,
            ["refResolved"] = refResolved,
            ["installedAt"] = installedAt.ToString("O")
        };
        if (subPath is not null) payload["subPath"] = subPath;
        var json = JsonSerializer.Serialize(payload, InstallMetadataJson);
        await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<InstallMetadata?> ReadInstallMetadataAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            return new InstallMetadata(
                root.TryGetProperty("source", out var s) ? s.GetString() : null,
                root.TryGetProperty("url", out var u) ? u.GetString() : null,
                root.TryGetProperty("ref", out var r) ? r.GetString() : null,
                root.TryGetProperty("subPath", out var sp) ? sp.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    private static void SafeDelete(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort: a leaked tmp dir is annoying but not destructive.
        }
    }

    private sealed record InstallMetadata(string? Source, string? Url, string? Ref, string? SubPath);
}

internal static partial class PackInstallServiceLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Installed pack '{PackId}' from {Url} @ {Ref} (sha={Sha}).")]
    public static partial void PackInstalled(this ILogger logger, string packId, string url, string @ref, string sha);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Updated pack '{PackId}' to ref {Ref} (sha={Sha}).")]
    public static partial void PackUpdated(this ILogger logger, string packId, string @ref, string sha);
}
