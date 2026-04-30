using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Default <see cref="IWidgetLibraryRegistry"/>: scans every directory in
/// <see cref="WidgetsOptions.LibrariesPaths"/> (and the back-compat
/// <see cref="WidgetsOptions.LibrariesPath"/>) for subdirectories
/// containing <c>manifest.json</c> + <c>widgets/&lt;kind&gt;/widget.json</c>
/// files. Symlinks at the top level are skipped so a malicious library
/// cannot escape its configured root via <c>/etc</c>.
/// </summary>
public sealed class FilesystemWidgetLibraryRegistry : IWidgetLibraryRegistry, IDisposable
{
    private readonly string[] _librariesPaths;
    private readonly int _maxLibraries;
    private readonly ILogger<FilesystemWidgetLibraryRegistry> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WidgetLibrary[]? _cache;

    public FilesystemWidgetLibraryRegistry(
        IOptions<WidgetsOptions> options,
        IHostEnvironment env,
        ILogger<FilesystemWidgetLibraryRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(logger);

        var raw = new List<string>(options.Value.LibrariesPaths.Count);
        foreach (var p in options.Value.LibrariesPaths)
        {
            if (!string.IsNullOrWhiteSpace(p)) raw.Add(p);
        }

        if (raw.Count == 0)
        {
            raw.Add(Path.Combine(env.ContentRootPath, "widget-libraries"));
        }

        // Resolve to absolute and dedupe — repeated paths in config would
        // double-load every library inside, then trip the collision check.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<string>(raw.Count);
        foreach (var p in raw)
        {
            var full = Path.GetFullPath(p);
            if (seen.Add(full)) resolved.Add(full);
        }

        _librariesPaths = [.. resolved];
        _maxLibraries = Math.Max(1, options.Value.MaxLibraries);
        _logger = logger;
    }

    /// <summary>The absolute paths the registry is watching, in scan order.
    /// Surfaced for diagnostics — never used as input from outside.</summary>
    public IReadOnlyList<string> LibrariesPaths => _librariesPaths;

    public async Task<IReadOnlyList<WidgetLibrary>> ListAsync(CancellationToken cancellationToken)
    {
        if (_cache is { } cached) return cached;
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        return _cache ?? [];
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
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
            // Dedupe across all scanned paths: if two paths surface a library
            // with the same manifest id, the first one wins. This is what
            // makes the layered-image pattern work — runtime path listed
            // first overrides the baked-in fallback for the same id.
            var libraries = new List<WidgetLibrary>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var capReached = false;

            foreach (var root in _librariesPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(root))
                {
                    _logger.LibrariesPathMissing(root);
                    continue;
                }

                var directories = Directory.EnumerateDirectories(root)
                    .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                    .ToArray();

                foreach (var dir in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (libraries.Count >= _maxLibraries)
                    {
                        _logger.LibraryCapReached(_maxLibraries, root);
                        capReached = true;
                        break;
                    }

                    // Reject top-level symlinks — they could point outside
                    // the configured root. Honest copies / mounts on the
                    // path are fine.
                    var info = new DirectoryInfo(dir);
                    if (info.LinkTarget is not null)
                    {
                        _logger.SymlinkSkipped(dir);
                        continue;
                    }

                    if (!TryLoadLibrary(dir, out var library)) continue;

                    if (!seenIds.Add(library.Id))
                    {
                        _logger.LibraryIdShadowed(library.Id, dir);
                        continue;
                    }

                    libraries.Add(library);
                }

                if (capReached) break;
            }

            _cache = libraries
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.Id, StringComparer.Ordinal)
                .ToArray();

            _logger.LibrariesLoaded(_cache.Length, _librariesPaths.Length, _librariesPaths[0]);
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryLoadLibrary(string dir, out WidgetLibrary library)
    {
        library = default!;
        var dirName = Path.GetFileName(dir);
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

        var (installSource, gitInfo) = ReadInstallMetadata(dir);

        library = new WidgetLibrary
        {
            Id = header.Id,
            Name = header.Name,
            Version = header.Version,
            Author = header.Author,
            License = header.License,
            Description = header.Description,
            InstallSource = installSource,
            GitUrl = gitInfo?.Url,
            GitRef = gitInfo?.Ref,
            GitRefResolved = gitInfo?.RefResolved,
            InstalledAt = gitInfo?.InstalledAt,
            Widgets = widgets
        };
        return true;
    }

    /// <summary>
    /// Reads <c>.install.json</c> if present. The file is created by the
    /// git installer (iter 4) and is absent for libraries dropped manually.
    /// Failures here are non-fatal — we just downgrade to filesystem source.
    /// </summary>
    private (LibraryInstallSource Source, GitInstallInfo? Git) ReadInstallMetadata(string dir)
    {
        var path = Path.Combine(dir, ".install.json");
        if (!File.Exists(path))
        {
            return (LibraryInstallSource.Filesystem, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (LibraryInstallSource.Filesystem, null);

            var src = root.TryGetProperty("source", out var sEl) && sEl.ValueKind == JsonValueKind.String
                ? sEl.GetString()
                : null;
            if (!string.Equals(src, "git", StringComparison.OrdinalIgnoreCase))
            {
                return (LibraryInstallSource.Filesystem, null);
            }

            var info = new GitInstallInfo(
                root.TryGetProperty("url", out var u) ? u.GetString() : null,
                root.TryGetProperty("ref", out var r) ? r.GetString() : null,
                root.TryGetProperty("refResolved", out var rr) ? rr.GetString() : null,
                root.TryGetProperty("installedAt", out var i) && i.TryGetDateTimeOffset(out var dt) ? dt : null);
            return (LibraryInstallSource.Git, info);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.InstallMetadataUnreadable(ex, dir);
            return (LibraryInstallSource.Filesystem, null);
        }
    }

    private sealed record GitInstallInfo(string? Url, string? Ref, string? RefResolved, DateTimeOffset? InstalledAt);
}

internal static partial class FilesystemWidgetLibraryRegistryLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Widget libraries path {Path} does not exist; treating registry as empty.")]
    public static partial void LibrariesPathMissing(this ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Widget libraries cap of {Cap} reached; skipping additional directories under {Path}.")]
    public static partial void LibraryCapReached(this ILogger logger, int cap, string path);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: top-level symlinks under the libraries path are not allowed.")]
    public static partial void SymlinkSkipped(this ILogger logger, string dir);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information,
        Message = "Loaded {Count} widget library/ies from {PathCount} configured path(s) (first: {FirstPath}).")]
    public static partial void LibrariesLoaded(this ILogger logger, int count, int pathCount, string firstPath);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: missing manifest.json.")]
    public static partial void MissingManifest(this ILogger logger, string dir);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: failed to read manifest.json.")]
    public static partial void ManifestReadFailed(this ILogger logger, Exception ex, string dir);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning,
        Message = "Skipping {Dir}: {Error}")]
    public static partial void ManifestRejected(this ILogger logger, string dir, string? error);

    [LoggerMessage(EventId = 8, Level = LogLevel.Warning,
        Message = "Library {Lib}: widget directory {Kind} has no widget.json; skipping.")]
    public static partial void WidgetJsonMissing(this ILogger logger, string lib, string kind);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning,
        Message = "Library {Lib}: failed to read widget {Kind}/widget.json; skipping.")]
    public static partial void WidgetReadFailed(this ILogger logger, Exception ex, string lib, string kind);

    [LoggerMessage(EventId = 10, Level = LogLevel.Warning,
        Message = "Library {Lib}: skipping widget {Kind}: {Error}")]
    public static partial void WidgetRejected(this ILogger logger, string lib, string kind, string? error);

    [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
        Message = ".install.json under {Dir} could not be parsed; treating library as filesystem-installed.")]
    public static partial void InstallMetadataUnreadable(this ILogger logger, Exception ex, string dir);

    [LoggerMessage(EventId = 12, Level = LogLevel.Warning,
        Message = "Library id '{LibId}' from {Dir} is already exposed by an earlier path in the scan order; skipping.")]
    public static partial void LibraryIdShadowed(this ILogger logger, string libId, string dir);
}
