using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for an installed pack, listed by <c>GET /api/v1/packs</c>.
/// Carries the shipping metadata (version, author, license, install
/// provenance) plus a flat catalog of the libraries and dashboards the
/// pack contains. The picker continues to consume libraries via
/// <c>GET /api/v1/widgets/libraries</c>; this endpoint is for the pack
/// management surface.
/// </summary>
public sealed record PackDto(
    string Id,
    string Name,
    string Version,
    string? Author,
    string? License,
    string? Description,
    string? Homepage,
    PackInstallSource InstallSource,
    string? GitUrl,
    string? GitRef,
    string? GitRefResolved,
    string? GitSubPath,
    DateTimeOffset? InstalledAt,
    bool Removable,
    IReadOnlyList<WidgetLibraryDto> Libraries,
    IReadOnlyList<PackDashboardDto> Dashboards,
    IReadOnlyList<PackIconDto> Icons);

/// <summary>
/// Wire shape for a dashboard shipped inside a pack. <c>builtin: true</c>
/// entries are seeded into the dashboards store on first boot;
/// <c>builtin: false</c> entries are installable templates the user can
/// clone into their own dashboard.
/// </summary>
public sealed record PackDashboardDto(string Id, bool Builtin);

/// <summary>
/// Wire shape for one pack-supplied icon — a service-map glyph the SPA
/// can render in place of the default shape when one of the
/// <see cref="Match"/> rules hits a node. <see cref="ImageUrl"/> is
/// already resolved server-side so the SPA never has to know how the
/// pack stores assets on disk.
/// </summary>
public sealed record PackIconDto(
    string Id,
    string Name,
    string ImageUrl,
    IReadOnlyList<PackIconMatchDto> Match);

/// <summary>One matcher entry inside <see cref="PackIconDto.Match"/>.
/// Exactly one of <see cref="ServiceName"/> /
/// <see cref="NamePattern"/> is set; the SPA resolver evaluates them
/// in declaration order.</summary>
public sealed record PackIconMatchDto(string? ServiceName, string? NamePattern);
