namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Read-side port for widget libraries discovered on disk. The registry is
/// a singleton with an in-memory cache populated on demand. Cache
/// invalidation is always explicit — there is no <c>FileSystemWatcher</c>
/// or background sync; libraries refresh only on
/// <see cref="ReloadAsync"/>, on a successful install/update/uninstall,
/// or on the first call after process start.
/// </summary>
public interface IWidgetLibraryRegistry
{
    /// <summary>
    /// All libraries currently exposed by the registry, ordered by name.
    /// First call hydrates from disk; subsequent calls hit the cache.
    /// </summary>
    Task<IReadOnlyList<WidgetLibrary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Force a re-scan of the libraries path. Replaces the cache atomically
    /// once the scan completes.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken);
}
