using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for a discovered widget library, listed by
/// <c>GET /api/v1/widgets/libraries</c>.
/// </summary>
public sealed record WidgetLibraryDto(
    string Id,
    string Name,
    string Version,
    string? Author,
    string? License,
    string? Description,
    LibraryInstallSource InstallSource,
    string? GitUrl,
    string? GitRef,
    string? GitRefResolved,
    DateTimeOffset? InstalledAt,
    IReadOnlyList<LibraryWidgetDto> Widgets);
