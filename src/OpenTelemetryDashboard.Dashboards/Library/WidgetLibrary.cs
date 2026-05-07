namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// A widget library inside a <see cref="Pack"/>. Loaded by
/// <see cref="IPackRegistry"/> on demand and cached in memory — never
/// round-tripped through the database. Library-level metadata stays
/// minimal: pack-shipping concerns (version, author, license, install
/// source) live on the parent pack, not duplicated here.
/// </summary>
public sealed class WidgetLibrary
{
    /// <summary>Library identifier — must match the directory name on
    /// disk and the <c>id</c> field in <c>manifest.json</c>; mismatches
    /// cause the library to be skipped at load time.</summary>
    public required string Id { get; init; }

    /// <summary>Display name shown as the picker section header.</summary>
    public required string Name { get; init; }

    /// <summary>Optional human-readable summary surfaced as a tooltip on
    /// the picker section header.</summary>
    public string? Description { get; init; }

    /// <summary>Optional <c>i-ph-…</c> / <c>i-lucide-…</c> icon glyph
    /// shown next to the picker section header.</summary>
    public string? Icon { get; init; }

    /// <summary>Id of the <see cref="Pack"/> this library lives in.
    /// Lets the SPA group libraries by pack in the management UI without
    /// a second lookup.</summary>
    public required string PackId { get; init; }

    /// <summary>Absolute path of the directory the library was loaded
    /// from. Internal detail used by uninstall — never serialized over
    /// the wire so we don't leak server filesystem layout to clients.</summary>
    internal string RootPath { get; init; } = string.Empty;

    public IReadOnlyList<LibraryWidget> Widgets { get; init; } = [];
}
