using System.Text.Json;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for a custom widget definition. <see cref="Config"/> and
/// <see cref="Spec"/> are surfaced as <see cref="JsonElement"/> so clients
/// consume their own typed shape without a double JSON encode.
/// </summary>
public sealed record WidgetDefinitionDto(
    Guid Id,
    string Name,
    string? Description,
    string Icon,
    WidgetEngine Engine,
    string? BaseKind,
    JsonElement Config,
    JsonElement? Spec,
    int DefaultW,
    int DefaultH,
    DateTimeOffset UpdatedAt,
    uint RowVersion);
