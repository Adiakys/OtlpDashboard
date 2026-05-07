namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Read-side port for packs discovered on disk. The registry is a
/// singleton with an in-memory cache populated on demand. Cache
/// invalidation is always explicit — there is no <c>FileSystemWatcher</c>
/// or background sync; packs refresh only on
/// <see cref="ReloadAsync"/>, on a successful install/update/uninstall,
/// or on the first call after process start.
/// </summary>
public interface IPackRegistry
{
    /// <summary>
    /// All packs currently exposed by the registry, ordered by name.
    /// First call hydrates from disk; subsequent calls hit the cache.
    /// </summary>
    Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Force a re-scan of every pack path. Replaces the cache atomically
    /// once the scan completes.
    /// </summary>
    Task ReloadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Permanently remove the pack directory from disk and refresh the
    /// cache. Throws <see cref="PackNotFoundException"/> if the id
    /// isn't currently registered, or <see cref="PackNotRemovableException"/>
    /// if the pack lives outside the runtime-managed root.
    /// </summary>
    Task UninstallAsync(string packId, CancellationToken cancellationToken);
}

/// <summary>
/// Read-side port for the flat list of widget libraries the picker
/// consumes. Implemented as an adapter over <see cref="IPackRegistry"/>:
/// every library across every pack is surfaced here, deduplicated by
/// id (first-wins on collision) so the picker has a single contract
/// to bind against.
/// </summary>
public interface IWidgetLibraryRegistry
{
    Task<IReadOnlyList<WidgetLibrary>> ListAsync(CancellationToken cancellationToken);
}
