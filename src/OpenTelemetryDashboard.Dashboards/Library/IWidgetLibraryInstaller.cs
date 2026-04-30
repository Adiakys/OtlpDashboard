namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Write-side port for widget libraries. Sits next to
/// <see cref="IWidgetLibraryRegistry"/> (read-side) so the read path stays
/// independent of the install machinery — the registry just sees a new
/// directory once the installer's atomic move completes.
/// </summary>
public interface IWidgetLibraryInstaller
{
    /// <summary>
    /// Clone <paramref name="url"/> at <paramref name="gitRef"/> into the
    /// runtime-managed root and register it. The library id is derived
    /// from the cloned <c>manifest.json</c>; the directory is named after
    /// it. Throws <see cref="WidgetLibraryHostNotAllowedException"/>,
    /// <see cref="WidgetLibraryIdCollisionException"/>,
    /// <see cref="WidgetLibraryManifestInvalidException"/>, or
    /// <see cref="WidgetLibraryGitOperationException"/> on failure — the
    /// filesystem is left untouched in every case (rollback is part of the
    /// contract).
    /// </summary>
    Task<WidgetLibrary> InstallAsync(
        string url,
        string gitRef,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-pull a previously git-installed library and reset its working
    /// tree to the original ref. The <c>refResolved</c> field in
    /// <c>.install.json</c> is updated to the new HEAD SHA.
    /// </summary>
    Task<WidgetLibrary> UpdateAsync(
        string libraryId,
        CancellationToken cancellationToken);
}
