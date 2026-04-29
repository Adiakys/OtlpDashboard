using System.Text.Json;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for one placement of a widget on a dashboard. Mirrors the
/// domain entity but exposes <see cref="Config"/> as a structured JSON
/// element so clients don't have to JSON-encode their config inside another
/// JSON string. The backend treats the config as opaque — it stores the
/// raw text and round-trips it back unchanged.
/// </summary>
public sealed record DashboardWidgetDto(
    Guid Id,
    string Kind,
    int X,
    int Y,
    int W,
    int H,
    JsonElement Config);
