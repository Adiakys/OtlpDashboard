using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace OpenTelemetryDashboard.Dashboards.Seeding;

/// <summary>
/// Strict JSON parser for the dashboard seed envelope. Schema is the
/// same one the SPA's <c>DashboardLayoutIO.exportToFile</c> emits (see
/// <c>web/app/pages/dashboard/dashboardLayoutIO.ts:12-17</c>) plus an
/// optional top-level <c>id</c> string the seeder uses to pin the
/// generated dashboard. Invalid files are rejected as a whole — the
/// caller logs and moves on rather than partially importing.
/// </summary>
public static class DashboardSeedParser
{
    /// <summary>The version field every seed file must declare.</summary>
    public const int SupportedVersion = 1;

    /// <summary>Cap on the per-widget config payload — same ceiling as the
    /// custom-widget DB constraint.</summary>
    public const int MaxConfigBytes = 64 * 1024;

    /// <summary>Cap on the whole-file size. A dashboard with 50 widgets at
    /// the per-widget cap fits comfortably; larger files are almost
    /// certainly an authoring mistake.</summary>
    public const int MaxFileBytes = 1024 * 1024;

    private const int MaxNameLength = 128;
    private const int MaxKindLength = 128;
    private const int MaxGridX = 11;     // 0-based column on a 12-wide grid
    private const int MaxGridY = 999;    // generous: rows are unbounded in spirit
    private const int MaxGridW = 12;
    private const int MaxGridH = 50;

    public static bool TryParse(
        string json,
        [NotNullWhen(true)] out DashboardSeedFile? file,
        [NotNullWhen(false)] out string? error)
    {
        file = null;

        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaxFileBytes)
        {
            error = $"file is too large ({bytes} bytes); maximum is {MaxFileBytes} bytes.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"file is not valid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "root must be a JSON object.";
                return false;
            }

            // Version: required, must equal SupportedVersion.
            if (!root.TryGetProperty("version", out var versionEl)
                || versionEl.ValueKind != JsonValueKind.Number
                || !versionEl.TryGetInt32(out var version))
            {
                error = "'version' is required and must be an integer.";
                return false;
            }
            if (version != SupportedVersion)
            {
                error = $"'version' must be {SupportedVersion} (got {version}).";
                return false;
            }

            // Optional id.
            Guid? id = null;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
            {
                if (idEl.ValueKind != JsonValueKind.String || !Guid.TryParse(idEl.GetString(), out var parsedId))
                {
                    error = "'id' must be a Guid string when present.";
                    return false;
                }
                id = parsedId;
            }

            // Required name.
            if (!root.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            {
                error = "'name' is required and must be a string.";
                return false;
            }
            var name = nameEl.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "'name' must not be empty.";
                return false;
            }
            if (name.Length > MaxNameLength)
            {
                error = $"'name' must be at most {MaxNameLength} characters.";
                return false;
            }

            // Required widgets array.
            if (!root.TryGetProperty("widgets", out var widgetsEl) || widgetsEl.ValueKind != JsonValueKind.Array)
            {
                error = "'widgets' is required and must be an array.";
                return false;
            }

            var widgets = new List<DashboardSeedWidget>(widgetsEl.GetArrayLength());
            var index = 0;
            foreach (var widgetEl in widgetsEl.EnumerateArray())
            {
                if (!TryParseWidget(widgetEl, index, out var widget, out var widgetError))
                {
                    error = widgetError;
                    return false;
                }
                widgets.Add(widget);
                index++;
            }

            file = new DashboardSeedFile(id, name, widgets);
            error = null;
            return true;
        }
    }

    private static bool TryParseWidget(
        JsonElement el,
        int index,
        [NotNullWhen(true)] out DashboardSeedWidget? widget,
        [NotNullWhen(false)] out string? error)
    {
        widget = null;
        if (el.ValueKind != JsonValueKind.Object)
        {
            error = $"widget #{index}: must be a JSON object.";
            return false;
        }

        // id: required Guid string.
        if (!el.TryGetProperty("id", out var idEl)
            || idEl.ValueKind != JsonValueKind.String
            || !Guid.TryParse(idEl.GetString(), out var id))
        {
            error = $"widget #{index}: 'id' must be a Guid string.";
            return false;
        }

        // kind: required string, ≤ 128 chars.
        if (!el.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
        {
            error = $"widget #{index}: 'kind' is required and must be a string.";
            return false;
        }
        var kind = kindEl.GetString();
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > MaxKindLength)
        {
            error = $"widget #{index}: 'kind' must be 1–{MaxKindLength} characters.";
            return false;
        }

        // grid placement.
        if (!TryReadInt(el, "x", 0, MaxGridX, index, out var x, out error)) return false;
        if (!TryReadInt(el, "y", 0, MaxGridY, index, out var y, out error)) return false;
        if (!TryReadInt(el, "w", 1, MaxGridW, index, out var w, out error)) return false;
        if (!TryReadInt(el, "h", 1, MaxGridH, index, out var h, out error)) return false;

        // config: required object, ≤ 64 KiB raw.
        if (!el.TryGetProperty("config", out var configEl) || configEl.ValueKind != JsonValueKind.Object)
        {
            error = $"widget #{index}: 'config' is required and must be a JSON object.";
            return false;
        }
        var configRaw = configEl.GetRawText();
        var configBytes = Encoding.UTF8.GetByteCount(configRaw);
        if (configBytes > MaxConfigBytes)
        {
            error = $"widget #{index}: 'config' is too large ({configBytes} bytes); maximum is {MaxConfigBytes} bytes.";
            return false;
        }

        widget = new DashboardSeedWidget(id, kind, x, y, w, h, configRaw);
        error = null;
        return true;
    }

    private static bool TryReadInt(
        JsonElement el,
        string field,
        int min,
        int max,
        int widgetIndex,
        out int value,
        [NotNullWhen(false)] out string? error)
    {
        value = 0;
        if (!el.TryGetProperty(field, out var fieldEl)
            || fieldEl.ValueKind != JsonValueKind.Number
            || !fieldEl.TryGetInt32(out value))
        {
            error = $"widget #{widgetIndex}: '{field}' must be an integer.";
            return false;
        }
        if (value < min || value > max)
        {
            error = $"widget #{widgetIndex}: '{field}' must be between {min} and {max}.";
            return false;
        }
        error = null;
        return true;
    }
}
