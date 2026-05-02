using System.Text.Json;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for a single library-provided widget. The fully-qualified
/// <c>kind</c> the SPA composes from this is
/// <c>library:&lt;libraryId&gt;/&lt;KindId&gt;</c>.
/// </summary>
public sealed record LibraryWidgetDto(
    string KindId,
    string Name,
    string? Description,
    string Icon,
    WidgetEngine Engine,
    string? BaseKind,
    JsonElement? Config,
    JsonElement? Spec,
    JsonElement? Parameters,
    int DefaultW,
    int DefaultH);
