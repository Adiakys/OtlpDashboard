namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// A pack discovered on disk: the unit of distribution for widget
/// libraries and dashboards. Loaded by <see cref="IPackRegistry"/>
/// from each scanned path; the runtime-managed root (first path in
/// scan order) holds removable packs, baked-in image layers carry
/// read-only ones.
/// </summary>
public sealed class Pack
{
    /// <summary>Pack identifier — must match the directory name on
    /// disk and the <c>id</c> field in <c>pack.json</c>.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public string? Author { get; init; }

    public string? License { get; init; }

    public string? Description { get; init; }

    /// <summary>Optional https URL for the pack's homepage / source
    /// repository — surfaced as an icon link in the pack manager UI.</summary>
    public string? Homepage { get; init; }

    public PackInstallSource InstallSource { get; init; } = PackInstallSource.Filesystem;

    /// <summary>For <see cref="PackInstallSource.Git"/>: the URL the
    /// pack was cloned from.</summary>
    public string? GitUrl { get; init; }

    /// <summary>For <see cref="PackInstallSource.Git"/>: the
    /// user-supplied ref (tag / branch / SHA).</summary>
    public string? GitRef { get; init; }

    /// <summary>For <see cref="PackInstallSource.Git"/>: the resolved
    /// commit SHA at install/update time.</summary>
    public string? GitRefResolved { get; init; }

    /// <summary>Optional sub-path inside the cloned repo where the
    /// pack root lives (for "monorepo of packs" installs).</summary>
    public string? GitSubPath { get; init; }

    /// <summary>For <see cref="PackInstallSource.Git"/>: timestamp of
    /// the last install or update.</summary>
    public DateTimeOffset? InstalledAt { get; init; }

    /// <summary>Absolute path of the pack's directory on disk. Internal
    /// detail used by uninstall and update — never serialized.</summary>
    internal string RootPath { get; init; } = string.Empty;

    /// <summary>True when the registry can delete this pack on
    /// uninstall. Set only for packs in the first configured path
    /// (the runtime-managed root). Baked-in packs are read-only.</summary>
    public bool Removable { get; init; }

    public IReadOnlyList<WidgetLibrary> Libraries { get; init; } = [];

    public IReadOnlyList<PackDashboard> Dashboards { get; init; } = [];

    public IReadOnlyList<PackIcon> Icons { get; init; } = [];
}
