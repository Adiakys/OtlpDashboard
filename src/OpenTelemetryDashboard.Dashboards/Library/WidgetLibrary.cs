namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// A widget pack discovered on disk. Loaded by
/// <see cref="IWidgetLibraryRegistry"/> on demand and cached in memory —
/// never round-tripped through the database.
/// </summary>
public sealed class WidgetLibrary
{
    /// <summary>
    /// Library identifier. Must match the directory name on disk and the
    /// <c>id</c> field in <c>manifest.json</c> — discrepancies cause the
    /// library to be skipped at load time.
    /// </summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public string? Author { get; init; }

    public string? License { get; init; }

    public string? Description { get; init; }

    /// <summary>Origin of the library directory.</summary>
    public LibraryInstallSource InstallSource { get; init; } = LibraryInstallSource.Filesystem;

    /// <summary>
    /// Absolute path of the directory the library was loaded from. Internal
    /// detail used by uninstall — never serialized over the wire so we
    /// don't leak server filesystem layout to clients.
    /// </summary>
    internal string RootPath { get; init; } = string.Empty;

    /// <summary>
    /// True when the registry can delete this library on uninstall. Set to
    /// true only for libraries living in the first configured path (the
    /// runtime-managed root). Baked-in libraries shipped via image layers
    /// are read-only by convention.
    /// </summary>
    public bool Removable { get; init; }

    /// <summary>
    /// For <see cref="LibraryInstallSource.Git"/>: the URL the library was
    /// cloned from. Null otherwise. Read from <c>.install.json</c>.
    /// </summary>
    public string? GitUrl { get; init; }

    /// <summary>
    /// For <see cref="LibraryInstallSource.Git"/>: the user-supplied ref
    /// (tag / branch / SHA). Null otherwise.
    /// </summary>
    public string? GitRef { get; init; }

    /// <summary>
    /// For <see cref="LibraryInstallSource.Git"/>: the resolved commit SHA at
    /// install/update time. Null otherwise.
    /// </summary>
    public string? GitRefResolved { get; init; }

    /// <summary>
    /// For <see cref="LibraryInstallSource.Git"/>: timestamp of the last
    /// install or update. Null otherwise.
    /// </summary>
    public DateTimeOffset? InstalledAt { get; init; }

    public IReadOnlyList<LibraryWidget> Widgets { get; init; } = [];
}
