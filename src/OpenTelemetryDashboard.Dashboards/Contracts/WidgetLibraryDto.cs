namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for a discovered widget library, listed by
/// <c>GET /api/v1/widgets/libraries</c> (the picker contract). Pack
/// shipping metadata (version, author, license, install source) is
/// not duplicated here — the SPA reads it from <c>GET /api/v1/packs</c>
/// when needed, while the picker only cares about the section header.
/// </summary>
public sealed record WidgetLibraryDto(
    string Id,
    string Name,
    string? Description,
    string? Icon,
    /// <summary>Id of the parent pack — exposed so the picker can label
    /// libraries with their provenance and the management UI can link
    /// the section back to its pack entry.</summary>
    string PackId,
    IReadOnlyList<LibraryWidgetDto> Widgets);
