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
