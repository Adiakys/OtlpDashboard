using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Orchestrator for installing and updating widget libraries from git.
/// Drives <see cref="IGitInstaller"/> through the transactional pipeline
/// (clone-tmp → parse manifest → write .install.json → atomic move) and
/// invalidates the registry cache after each successful operation.
/// </summary>
public sealed partial class LibraryInstallService : IWidgetLibraryInstaller
{
    /// <summary>
    /// Schema version stamped into <c>.install.json</c>. Bump when the
    /// shape changes — load-side code that reads the file can branch on it.
    /// </summary>
    public const int InstallMetadataVersion = 1;

    private static readonly Regex IdRegex = new(
        @"^[a-z0-9](-?[a-z0-9])*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions InstallMetadataJson = new() { WriteIndented = true };

    private readonly IGitInstaller _git;
    private readonly IWidgetLibraryRegistry _registry;
    private readonly WidgetsOptions _options;
    private readonly string _primaryRoot;
    private readonly ILogger<LibraryInstallService> _logger;

    public LibraryInstallService(
        IGitInstaller git,
        IWidgetLibraryRegistry registry,
        IOptions<WidgetsOptions> options,
        FilesystemWidgetLibraryRegistry primaryPathProvider,
        ILogger<LibraryInstallService> logger)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(primaryPathProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _git = git;
        _registry = registry;
        _options = options.Value;
        _primaryRoot = primaryPathProvider.LibrariesPaths.Count > 0
            ? primaryPathProvider.LibrariesPaths[0]
            : throw new InvalidOperationException("No libraries path is configured.");
        _logger = logger;
    }

    public async Task<WidgetLibrary> InstallAsync(string url, string gitRef, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRef);

        ValidateUrl(url);

        Directory.CreateDirectory(_primaryRoot);

        // Clone into a hidden tmp directory so a failure mid-pipeline
        // doesn't surface a half-installed library to the registry's next
        // scan. The leading dot keeps it below alphabetical sort and the
        // random suffix is enough on a single-tenant install.
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
            throw new WidgetLibraryGitOperationException("Clone failed.", ex);
        }

        try
        {
            // Parse manifest before settling on the final directory name.
            // The header gives us the canonical id; the file may have been
            // authored in a folder with a different on-disk name.
            var manifestPath = Path.Combine(tmpDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new WidgetLibraryManifestInvalidException("manifest.json is missing.");
            }

            // The parser checks `id == expected`. We don't yet know the
            // canonical name, so do a permissive sniff first to lift the id
            // off, then run the strict parse with the matching expected id.
            var raw = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var sniffedId = SniffManifestId(raw);
            if (sniffedId is null || !IdRegex.IsMatch(sniffedId))
            {
                throw new WidgetLibraryManifestInvalidException("manifest.json: 'id' is missing or invalid.");
            }

            if (!LibraryManifestParser.TryParseManifest(raw, sniffedId, out _, out var parseError))
            {
                throw new WidgetLibraryManifestInvalidException(parseError ?? "manifest validation failed");
            }

            var targetDir = Path.Combine(_primaryRoot, sniffedId);
            if (Directory.Exists(targetDir))
            {
                throw new WidgetLibraryIdCollisionException(sniffedId);
            }

            // Pin the resolved SHA before the move so the metadata file is
            // ready when the next scan picks the directory up.
            var resolvedSha = _git.ResolveHead(tmpDir);
            var installedAt = DateTimeOffset.UtcNow;
            await WriteInstallMetadataAsync(tmpDir, url, gitRef, resolvedSha, installedAt, cancellationToken)
                .ConfigureAwait(false);

            // Atomic move keeps the registry from observing partial state.
            Directory.Move(tmpDir, targetDir);
            tmpDir = null!; // ownership transferred to the registry

            await _registry.ReloadAsync(cancellationToken).ConfigureAwait(false);
            _logger.LibraryInstalled(sniffedId, url, gitRef, resolvedSha);

            // Hand back the freshly-loaded library so the endpoint can
            // serialize it into the response.
            var libs = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);
            return libs.First(l => l.Id == sniffedId);
        }
        catch (WidgetLibraryInstallException)
        {
            SafeDelete(tmpDir);
            throw;
        }
        catch (Exception ex)
        {
            SafeDelete(tmpDir);
            throw new WidgetLibraryGitOperationException("Install pipeline failed.", ex);
        }
    }

    public async Task<WidgetLibrary> UpdateAsync(string libraryId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryId);
        if (!IdRegex.IsMatch(libraryId))
        {
            throw new WidgetLibraryNotFoundException(libraryId);
        }

        var libDir = Path.Combine(_primaryRoot, libraryId);
        if (!Directory.Exists(libDir))
        {
            throw new WidgetLibraryNotFoundException(libraryId);
        }

        var metaPath = Path.Combine(libDir, ".install.json");
        if (!File.Exists(metaPath))
        {
            throw new WidgetLibraryNotGitInstalledException(libraryId);
        }

        var meta = await ReadInstallMetadataAsync(metaPath, cancellationToken).ConfigureAwait(false);
        if (meta is null || !string.Equals(meta.Source, "git", StringComparison.OrdinalIgnoreCase))
        {
            throw new WidgetLibraryNotGitInstalledException(libraryId);
        }
        if (string.IsNullOrWhiteSpace(meta.Url) || string.IsNullOrWhiteSpace(meta.Ref))
        {
            throw new WidgetLibraryNotGitInstalledException(libraryId);
        }

        try
        {
            await _git.FetchAndResetAsync(libDir, meta.Ref,
                TimeSpan.FromSeconds(_options.GitInstallTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WidgetLibraryGitOperationException("Update failed.", ex);
        }

        var resolvedSha = _git.ResolveHead(libDir);
        var installedAt = DateTimeOffset.UtcNow;
        await WriteInstallMetadataAsync(libDir, meta.Url, meta.Ref, resolvedSha, installedAt, cancellationToken)
            .ConfigureAwait(false);

        await _registry.ReloadAsync(cancellationToken).ConfigureAwait(false);
        _logger.LibraryUpdated(libraryId, meta.Ref, resolvedSha);

        var libs = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);
        return libs.First(l => l.Id == libraryId);
    }

    private void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            throw new WidgetLibraryHostNotAllowedException(url);
        }
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new WidgetLibraryHostNotAllowedException(parsed.Scheme);
        }
        var host = parsed.Host.ToLowerInvariant();
        if (!_options.AllowedGitHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new WidgetLibraryHostNotAllowedException(host);
        }
    }

    private static string? SniffManifestId(string json)
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
        DateTimeOffset installedAt,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(dir, ".install.json");
        var payload = new
        {
            version = InstallMetadataVersion,
            source = "git",
            url,
            @ref = gitRef,
            refResolved,
            installedAt = installedAt.ToString("O")
        };
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
                root.TryGetProperty("ref", out var r) ? r.GetString() : null);
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

    private sealed record InstallMetadata(string? Source, string? Url, string? Ref);
}

internal static partial class LibraryInstallServiceLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Installed widget library '{LibId}' from {Url} @ {Ref} (sha={Sha}).")]
    public static partial void LibraryInstalled(this ILogger logger, string libId, string url, string @ref, string sha);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Updated widget library '{LibId}' to ref {Ref} (sha={Sha}).")]
    public static partial void LibraryUpdated(this ILogger logger, string libId, string @ref, string sha);
}
