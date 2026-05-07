namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Write-side port for packs. Sits next to <see cref="IPackRegistry"/>
/// so the read path stays independent of the install machinery — the
/// registry just sees a new directory once the installer's atomic move
/// completes.
/// </summary>
public interface IPackInstaller
{
    /// <summary>
    /// Clone <paramref name="url"/> at <paramref name="gitRef"/> into
    /// the runtime-managed root and register it as a pack. When
    /// <paramref name="subPath"/> is set the installer treats
    /// <c>&lt;clone&gt;/&lt;subPath&gt;</c> as the pack root — useful
    /// for monorepos that ship multiple packs side by side. The pack
    /// id is derived from the cloned <c>pack.json</c>; the on-disk
    /// directory is named after it. Throws
    /// <see cref="PackHostNotAllowedException"/>,
    /// <see cref="PackIdCollisionException"/>,
    /// <see cref="PackManifestInvalidException"/>,
    /// <see cref="PackInstallPathInvalidException"/>, or
    /// <see cref="PackGitOperationException"/> on failure — the
    /// filesystem is left untouched in every case (rollback is part
    /// of the contract).
    /// </summary>
    Task<Pack> InstallAsync(
        string url,
        string gitRef,
        string? subPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Re-pull a previously git-installed pack and reset its working
    /// tree to the original ref. The <c>refResolved</c> field in
    /// <c>.install.json</c> is updated to the new HEAD SHA.
    /// </summary>
    Task<Pack> UpdateAsync(string packId, CancellationToken cancellationToken);
}
