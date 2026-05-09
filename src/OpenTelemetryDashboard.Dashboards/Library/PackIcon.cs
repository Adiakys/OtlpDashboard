namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// One service-map icon shipped inside a <see cref="Pack"/>. Each icon
/// is a directory under the pack root containing an <c>icon.json</c>
/// descriptor and a single image file (SVG/PNG/WebP). The
/// <see cref="Match"/> rules are evaluated in array order against
/// service-map nodes; the first hit across all packs and icons wins.
/// </summary>
public sealed class PackIcon
{
    /// <summary>Icon identifier — must match the on-disk directory name
    /// and the <c>id</c> field in <c>icon.json</c>. Scoped to the pack;
    /// two packs may ship icons with the same id.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Image filename relative to the icon's directory
    /// (e.g. <c>postgres.svg</c>). The pack loader has already
    /// validated that the resolved absolute path stays inside the pack
    /// root and that the extension is in the asset whitelist.</summary>
    public required string ImageRelativePath { get; init; }

    /// <summary>Forward-slash path under the pack root, used to build
    /// the asset URL (<c>/api/v1/packs/{packId}/assets/{Image}</c>).
    /// Combines the icon directory with <see cref="ImageRelativePath"/>.</summary>
    public required string Image { get; init; }

    public required string ContentType { get; init; }

    public required IReadOnlyList<PackIconMatch> Match { get; init; }
}

/// <summary>
/// One matcher in <see cref="PackIcon.Match"/>. Exactly one field is
/// non-null; the parser enforces this at load time. Match types are
/// evaluated against the node's service name only in v1 — attribute
/// matching is deferred until the service-map reader exposes
/// per-node attributes.
/// </summary>
public sealed class PackIconMatch
{
    /// <summary>Exact match on the node's service name (case-sensitive).</summary>
    public string? ServiceName { get; init; }

    /// <summary>Regex pattern evaluated against the node's service
    /// name. Compiled at load time.</summary>
    public string? NamePattern { get; init; }
}
