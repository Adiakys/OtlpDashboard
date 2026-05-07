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
    IReadOnlyList<PackDashboardDto> Dashboards);

/// <summary>
/// Wire shape for a dashboard shipped inside a pack. <c>builtin: true</c>
/// entries are seeded into the dashboards store on first boot;
/// <c>builtin: false</c> entries are installable templates the user can
/// clone into their own dashboard.
/// </summary>
public sealed record PackDashboardDto(string Id, bool Builtin);
