using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Default <see cref="IPackRegistry"/>: scans every directory in
/// <see cref="PacksOptions.Paths"/> for sub-directories containing a
/// <c>pack.json</c>. Each pack's <c>libraries[]</c> are loaded via
/// <c>manifest.json</c> + <c>widgets/&lt;kind&gt;/widget.json</c>; each
/// pack's <c>dashboards[]</c> are loaded via the per-file JSON. Symlinks
/// at the top level are skipped so a malicious pack cannot escape its
/// configured root via <c>/etc</c>.
/// </summary>
public sealed class FilesystemPackRegistry : IPackRegistry, IDisposable
{
    private readonly string[] _packsPaths;
    private readonly int _maxPacks;
    private readonly ILogger<FilesystemPackRegistry> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Pack[]? _cache;

    public FilesystemPackRegistry(
        IOptions<PacksOptions> options,
        IHostEnvironment env,
        ILogger<FilesystemPackRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(logger);

        var raw = new List<string>(options.Value.Paths.Count);
        foreach (var p in options.Value.Paths)
        {
            if (!string.IsNullOrWhiteSpace(p)) raw.Add(p);
        }

        if (raw.Count == 0)
        {
            raw.Add(Path.Combine(env.ContentRootPath, "packs"));
        }

        // Resolve to absolute and dedupe — repeated paths in config would
        // double-load every pack inside, then trip the collision check.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<string>(raw.Count);
        foreach (var p in raw)
        {
            var full = Path.GetFullPath(p);
            if (seen.Add(full)) resolved.Add(full);
        }

        _packsPaths = [.. resolved];
        _maxPacks = Math.Max(1, options.Value.MaxPacks);
        _logger = logger;
    }

    /// <summary>Absolute paths the registry is watching, in scan order.
    /// Surfaced for diagnostics — never used as input from outside.</summary>
    public IReadOnlyList<string> PacksPaths => _packsPaths;

    public async Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached) return cached;
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache ?? [];
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UninstallAsync(string packId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        var match = _cache?.FirstOrDefault(p => string.Equals(p.Id, packId, StringComparison.Ordinal));
        if (match is null) throw new PackNotFoundException(packId);
        if (!match.Removable) throw new PackNotRemovableException(packId);

        // Defence in depth: re-verify the resolved path is contained in
        // the primary root. Any drift bails here instead of touching
        // files outside the managed dir.
        var primaryRoot = Path.GetFullPath(_packsPaths[0]);
        var packRoot = Path.GetFullPath(match.RootPath);
        var primaryWithSep = primaryRoot.EndsWith(Path.DirectorySeparatorChar)
            ? primaryRoot
            : primaryRoot + Path.DirectorySeparatorChar;
        if (!packRoot.StartsWith(primaryWithSep, StringComparison.Ordinal))
        {
            throw new PackNotRemovableException(packId);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(packRoot))
            {
                Directory.Delete(packRoot, recursive: true);
            }
            _cache = null;
        }
        finally
        {
            _gate.Release();
        }

        await LoadAsync(cancellationToken).ConfigureAwait(false);
        _logger.PackUninstalled(packId, packRoot);
    }

    public void Dispose() => _gate.Dispose();

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null) return;
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Dedupe across all scanned paths: if two paths surface a pack
            // with the same id, the first one wins. Layered-image pattern:
            // runtime path listed first overrides the baked-in fallback.
            var packs = new List<Pack>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var capReached = false;

            // Only the first configured path is "removable" — that's the
            // runtime-managed root. Subsequent paths are baked-in.
            var primaryRoot = _packsPaths[0];

            foreach (var root in _packsPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(root))
                {
                    _logger.PacksPathMissing(root);
                    continue;
                }

                var isPrimary = string.Equals(root, primaryRoot, StringComparison.Ordinal);

                var directories = Directory.EnumerateDirectories(root)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                    .ToArray();

                foreach (var dir in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (packs.Count >= _maxPacks)
                    {
                        _logger.PackCapReached(_maxPacks, root);
                        capReached = true;
                        break;
                    }

                    var info = new DirectoryInfo(dir);
                    if (info.LinkTarget is not null)
                    {
                        _logger.SymlinkSkipped(dir);
                        continue;
                    }

                    if (!TryLoadPack(dir, isPrimary, out var pack)) continue;
                    if (!seenIds.Add(pack.Id))
                    {
                        _logger.PackIdShadowed(pack.Id, dir);
                        continue;
                    }

                    packs.Add(pack);
                }

                if (capReached) break;
            }

            _cache = packs
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id, StringComparer.Ordinal)
                .ToArray();

            _logger.PacksLoaded(_cache.Length, _packsPaths.Length, _packsPaths[0]);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryLoadPack(string dir, bool removable, out Pack pack)
    {
        pack = default!;
        var dirName = Path.GetFileName(dir);
        var packPath = Path.Combine(dir, "pack.json");
        if (!File.Exists(packPath))
        {
            _logger.MissingPackManifest(dir);
            return false;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(packPath);
        }
        catch (IOException ex)
        {
            _logger.PackManifestReadFailed(ex, dir);
            return false;
        }

        if (!LibraryManifestParser.TryParsePack(raw, dirName, out var packManifest, out var err))
        {
            _logger.PackManifestRejected(dir, err);
            return false;
        }

        // Resolve every relative path against the pack root and confirm
        // containment — the parser already rejected `..` and absolute
        // prefixes, but a symlink pointing outside is still a possible
        // attack vector here.
        var packRoot = Path.GetFullPath(dir);
        var packRootWithSep = packRoot.EndsWith(Path.DirectorySeparatorChar)
            ? packRoot
            : packRoot + Path.DirectorySeparatorChar;

        var libraries = new List<WidgetLibrary>(packManifest.Libraries.Count);
        foreach (var libRef in packManifest.Libraries)
        {
            var libDirAbs = Path.GetFullPath(Path.Combine(dir, libRef.RelativePath));
            if (!libDirAbs.StartsWith(packRootWithSep, StringComparison.Ordinal))
            {
                _logger.PackPathEscaped(packManifest.Id, libRef.Id, libDirAbs);
                continue;
            }
            if (!Directory.Exists(libDirAbs))
            {
                _logger.PackLibraryMissing(packManifest.Id, libRef.Id, libDirAbs);
                continue;
            }

            if (!TryLoadLibrary(libDirAbs, packManifest.Id, out var library)) continue;
            if (!string.Equals(library.Id, libRef.Id, StringComparison.Ordinal))
            {
                _logger.PackLibraryIdMismatch(packManifest.Id, libRef.Id, library.Id);
                continue;
            }

            libraries.Add(library);
        }

        var dashboards = new List<PackDashboard>(packManifest.Dashboards.Count);
        foreach (var dashRef in packManifest.Dashboards)
        {
            var dashFileAbs = Path.GetFullPath(Path.Combine(dir, dashRef.RelativePath));
            if (!dashFileAbs.StartsWith(packRootWithSep, StringComparison.Ordinal))
            {
                _logger.PackPathEscaped(packManifest.Id, dashRef.Id, dashFileAbs);
                continue;
            }
            if (!File.Exists(dashFileAbs))
            {
                _logger.PackDashboardMissing(packManifest.Id, dashRef.Id, dashFileAbs);
                continue;
            }

            string dashRaw;
            try
            {
                dashRaw = File.ReadAllText(dashFileAbs);
            }
            catch (IOException ex)
            {
                _logger.PackDashboardReadFailed(ex, packManifest.Id, dashRef.Id);
                continue;
            }

            // Lightweight schema check: the seeder will re-validate
            // strictly with the dashboard parser. Catching obvious JSON
            // breakage here keeps the registry's diagnostics close to
            // the file the user just edited.
            try
            {
                using var probe = JsonDocument.Parse(dashRaw);
            }
            catch (JsonException ex)
            {
                _logger.PackDashboardReadFailed(ex, packManifest.Id, dashRef.Id);
                continue;
            }

            dashboards.Add(new PackDashboard
            {
                Id = dashRef.Id,
                SourcePath = dashFileAbs,
                RawJson = dashRaw,
                Builtin = dashRef.Builtin,
            });
        }

        var (installSource, gitInfo) = ReadInstallMetadata(dir);

        pack = new Pack
        {
            Id = packManifest.Id,
            Name = packManifest.Name,
            Version = packManifest.Version,
            Author = packManifest.Author,
            License = packManifest.License,
            Description = packManifest.Description,
            Homepage = packManifest.Homepage,
            InstallSource = installSource,
            GitUrl = gitInfo?.Url,
            GitRef = gitInfo?.Ref,
            GitRefResolved = gitInfo?.RefResolved,
            GitSubPath = gitInfo?.SubPath,
            InstalledAt = gitInfo?.InstalledAt,
            RootPath = packRoot,
            Removable = removable,
            Libraries = libraries,
            Dashboards = dashboards,
        };
        return true;
    }

    private bool TryLoadLibrary(string dir, string packId, out WidgetLibrary library)
    {
        library = default!;
        var dirName = Path.GetFileName(dir)!;
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            _logger.MissingManifest(dir);
            return false;
        }

        string manifestRaw;
        try
        {
            manifestRaw = File.ReadAllText(manifestPath);
        }
        catch (IOException ex)
        {
            _logger.ManifestReadFailed(ex, dir);
            return false;
        }

        if (!LibraryManifestParser.TryParseManifest(manifestRaw, dirName, out var header, out var manifestError))
        {
            _logger.ManifestRejected(dir, manifestError);
            return false;
        }

        var widgets = new List<LibraryWidget>();
        var widgetsDir = Path.Combine(dir, "widgets");
        if (Directory.Exists(widgetsDir))
        {
            foreach (var widgetDir in Directory.EnumerateDirectories(widgetsDir).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
            {
                var kindId = Path.GetFileName(widgetDir);
                var widgetJson = Path.Combine(widgetDir, "widget.json");
                if (!File.Exists(widgetJson))
                {
                    _logger.WidgetJsonMissing(header.Id, kindId);
                    continue;
                }

                string raw;
                try
                {
                    raw = File.ReadAllText(widgetJson);
                }
                catch (IOException ex)
                {
                    _logger.WidgetReadFailed(ex, header.Id, kindId);
                    continue;
                }

                if (!LibraryManifestParser.TryParseWidget(raw, kindId, out var widget, out var widgetError))
                {
                    _logger.WidgetRejected(header.Id, kindId, widgetError);
                    continue;
                }

                widgets.Add(widget);
            }
        }

        library = new WidgetLibrary
        {
            Id = header.Id,
            Name = header.Name,
            Description = header.Description,
            Icon = header.Icon,
            PackId = packId,
            RootPath = Path.GetFullPath(dir),
            Widgets = widgets,
        };
        return true;
    }

    /// <summary>
    /// Reads <c>.install.json</c> if present. The file is created by the
    /// pack installer and is absent for packs dropped manually.
    /// Failures here are non-fatal — we just downgrade to filesystem source.
    /// </summary>
    private (PackInstallSource Source, GitInstallInfo? Git) ReadInstallMetadata(string dir)
    {
        var path = Path.Combine(dir, ".install.json");
        if (!File.Exists(path))
        {
            return (PackInstallSource.Filesystem, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (PackInstallSource.Filesystem, null);

            var src = root.TryGetProperty("source", out var sEl) && sEl.ValueKind == JsonValueKind.String
                ? sEl.GetString()
                : null;
            if (!string.Equals(src, "git", StringComparison.OrdinalIgnoreCase))
            {
                return (PackInstallSource.Filesystem, null);
            }

            var info = new GitInstallInfo(
                root.TryGetProperty("url", out var u) ? u.GetString() : null,
                root.TryGetProperty("ref", out var r) ? r.GetString() : null,
                root.TryGetProperty("refResolved", out var rr) ? rr.GetString() : null,
                root.TryGetProperty("subPath", out var sp) ? sp.GetString() : null,
                root.TryGetProperty("installedAt", out var i) && i.TryGetDateTimeOffset(out var dt) ? dt : null);
            return (PackInstallSource.Git, info);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.InstallMetadataUnreadable(ex, dir);
            return (PackInstallSource.Filesystem, null);
        }
    }

    private sealed record GitInstallInfo(string? Url, string? Ref, string? RefResolved, string? SubPath, DateTimeOffset? InstalledAt);
}

/// <summary>
/// Adapter exposing the flat list of widget libraries the picker
/// consumes. Built on top of <see cref="IPackRegistry"/> so packs and
/// libraries always agree on the same source of truth.
/// </summary>
public sealed class WidgetLibraryRegistryAdapter : IWidgetLibraryRegistry
{
    private readonly IPackRegistry _packs;

    public WidgetLibraryRegistryAdapter(IPackRegistry packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        _packs = packs;
    }

    public async Task<IReadOnlyList<WidgetLibrary>> ListAsync(CancellationToken cancellationToken)
    {
        var packs = await _packs.ListAsync(cancellationToken).ConfigureAwait(false);
        // First-wins on library id collision across packs — consistent
        // with how packs themselves are deduped at the registry level.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var libs = new List<WidgetLibrary>();
        foreach (var pack in packs)
        {
            foreach (var lib in pack.Libraries)
            {
                if (!seen.Add(lib.Id)) continue;
                libs.Add(lib);
            }
        }
        return libs;
    }
}

internal static partial class FilesystemPackRegistryLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Packs path {Path} does not exist; treating registry as empty.")]
    public static partial void PacksPathMissing(this ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Pack cap of {Cap} reached; skipping additional directories under {Path}.")]
    public static partial void PackCapReached(this ILogger logger, int cap, string path);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: top-level symlinks under the packs path are not allowed.")]
    public static partial void SymlinkSkipped(this ILogger logger, string dir);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information,
        Message = "Loaded {Count} pack(s) from {PathCount} configured path(s) (first: {FirstPath}).")]
    public static partial void PacksLoaded(this ILogger logger, int count, int pathCount, string firstPath);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: missing pack.json.")]
    public static partial void MissingPackManifest(this ILogger logger, string dir);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Skipping pack {Dir}: failed to read pack.json.")]
    public static partial void PackManifestReadFailed(this ILogger logger, Exception ex, string dir);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Skipping pack {Dir}: {Error}")]
    public static partial void PackManifestRejected(this ILogger logger, string dir, string? error);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Pack id '{PackId}' from {Dir} is already exposed by an earlier path; skipping.")]
    public static partial void PackIdShadowed(this ILogger logger, string packId, string dir);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Pack '{PackId}': asset '{AssetId}' resolves outside the pack root ({Path}); skipping.")]
    public static partial void PackPathEscaped(this ILogger logger, string packId, string assetId, string path);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Pack '{PackId}': library '{LibId}' is missing on disk ({Path}); skipping.")]
    public static partial void PackLibraryMissing(this ILogger logger, string packId, string libId, string path);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
        Message = "Pack '{PackId}': library '{Declared}' has manifest id '{Found}' that doesn't match; skipping.")]
    public static partial void PackLibraryIdMismatch(this ILogger logger, string packId, string declared, string found);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "Pack '{PackId}': dashboard '{DashId}' is missing on disk ({Path}); skipping.")]
    public static partial void PackDashboardMissing(this ILogger logger, string packId, string dashId, string path);

    [LoggerMessage(EventId = 13, Level = LogLevel.Warning,
        Message = "Pack '{PackId}': dashboard '{DashId}' could not be parsed.")]
    public static partial void PackDashboardReadFailed(this ILogger logger, Exception ex, string packId, string dashId);

    [LoggerMessage(EventId = 14, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: missing manifest.json.")]
    public static partial void MissingManifest(this ILogger logger, string dir);

    [LoggerMessage(EventId = 15, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: failed to read manifest.json.")]
    public static partial void ManifestReadFailed(this ILogger logger, Exception ex, string dir);

    [LoggerMessage(EventId = 16, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: {Error}")]
    public static partial void ManifestRejected(this ILogger logger, string dir, string? error);

    [LoggerMessage(EventId = 17, Level = LogLevel.Warning,
        Message = "Library {Lib}: widget directory {Kind} has no widget.json; skipping.")]
    public static partial void WidgetJsonMissing(this ILogger logger, string lib, string kind);

    [LoggerMessage(EventId = 18, Level = LogLevel.Warning,
        Message = "Library {Lib}: failed to read widget {Kind}/widget.json; skipping.")]
    public static partial void WidgetReadFailed(this ILogger logger, Exception ex, string lib, string kind);

    [LoggerMessage(EventId = 19, Level = LogLevel.Warning,
        Message = "Library {Lib}: skipping widget {Kind}: {Error}")]
    public static partial void WidgetRejected(this ILogger logger, string lib, string kind, string? error);

    [LoggerMessage(EventId = 20, Level = LogLevel.Warning,
        Message = ".install.json under {Dir} could not be parsed; treating pack as filesystem-installed.")]
    public static partial void InstallMetadataUnreadable(this ILogger logger, Exception ex, string dir);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information,
        Message = "Uninstalled pack '{PackId}' (deleted {Path}).")]
    public static partial void PackUninstalled(this ILogger logger, string packId, string path);
}
