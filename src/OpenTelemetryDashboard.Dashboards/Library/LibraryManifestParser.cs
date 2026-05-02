using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Strict JSON parser for <c>manifest.json</c> and per-widget
/// <c>widget.json</c> files. The parser never executes user code and never
/// trusts inputs blindly: every string is bounded, every payload is size-
/// capped, and unknown shapes are rejected with a human-readable diagnostic
/// the registry surfaces in the logs.
/// </summary>
public static class LibraryManifestParser
{
    /// <summary>
    /// Builtin kinds a library widget with <c>engine: preset</c> may wrap.
    /// Mirrors the SPA's static registry; keep in sync with
    /// <c>web/app/pages/dashboard/registry.ts</c>.
    /// </summary>
    public static readonly IReadOnlySet<string> BuiltinKinds =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "metric-stat",
            "metric-line",
            "metric-sparkline",
            "metric-gauge",
            "metric-bar-gauge",
            "metric-pie",
            "metric-heatmap",
            "recent-traces",
            "logs-stream",
            "text"
        };

    private const int MaxIdLength = 64;
    private const int MaxNameLength = 64;
    private const int MaxVersionLength = 32;
    private const int MaxAuthorLength = 128;
    private const int MaxLicenseLength = 64;
    private const int MaxDescriptionLength = 280;
    private const int MaxIconLength = 64;
    private const int MaxBaseKindLength = 64;

    /// <summary>Per-widget config payload size cap. Aligns with the
    /// custom-widget DB constraint so libraries and DB-backed customs share a
    /// single ceiling.</summary>
    public const int MaxConfigBytes = SaveWidgetDefinitionRequest.MaxConfigBytes;

    /// <summary>Per-widget spec payload size cap (Vega-Lite specs).</summary>
    public const int MaxSpecBytes = SaveWidgetDefinitionRequest.MaxSpecBytes;

    private static readonly Regex IdRegex = new(
        @"^[a-z0-9](-?[a-z0-9])*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex IconRegex = new(
        @"^i-(ph|lucide)-[a-z0-9]+(-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parsed manifest header. Drives the picker section name in the SPA;
    /// widgets come in via <see cref="TryParseWidget"/>.
    /// </summary>
    public sealed record ManifestHeader(
        string Id,
        string Name,
        string Version,
        string? Author,
        string? License,
        string? Description);

    /// <summary>
    /// Parse a <c>manifest.json</c> payload. The expected directory name is
    /// passed in so the caller can verify <c>id</c> matches the on-disk name
    /// (a security requirement before the value is used as a path component).
    /// </summary>
    public static bool TryParseManifest(
        string json,
        string expectedId,
        [NotNullWhen(true)] out ManifestHeader? header,
        [NotNullWhen(false)] out string? error)
    {
        header = null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"manifest.json is not valid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "manifest.json must be a JSON object.";
                return false;
            }

            if (!TryRequiredString(root, "id", MaxIdLength, out var id, out error)) return false;
            if (!IdRegex.IsMatch(id))
            {
                error = "manifest.json: 'id' must match [a-z0-9-] (lowercase, no slashes, no dots).";
                return false;
            }
            if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                error = $"manifest.json: 'id' ('{id}') does not match the directory name ('{expectedId}').";
                return false;
            }

            if (!TryRequiredString(root, "name", MaxNameLength, out var name, out error)) return false;
            if (!TryRequiredString(root, "version", MaxVersionLength, out var version, out error)) return false;

            if (!TryOptionalString(root, "author", MaxAuthorLength, out var author, out error)) return false;
            if (!TryOptionalString(root, "license", MaxLicenseLength, out var license, out error)) return false;
            if (!TryOptionalString(root, "description", MaxDescriptionLength, out var description, out error)) return false;

            header = new ManifestHeader(id, name, version, author, license, description);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Parse a <c>widget.json</c> payload. <paramref name="kindId"/> is the
    /// directory name the widget lives under; the parser doesn't itself
    /// touch the filesystem.
    /// </summary>
    public static bool TryParseWidget(
        string json,
        string kindId,
        [NotNullWhen(true)] out LibraryWidget? widget,
        [NotNullWhen(false)] out string? error)
    {
        widget = null;

        if (!IdRegex.IsMatch(kindId) || kindId.Length > MaxIdLength)
        {
            error = $"Widget directory name '{kindId}' must match [a-z0-9-] and be at most {MaxIdLength} chars.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"widget.json is not valid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "widget.json must be a JSON object.";
                return false;
            }

            if (!TryRequiredString(root, "name", MaxNameLength, out var name, out error)) return false;
            if (!TryOptionalString(root, "description", MaxDescriptionLength, out var description, out error)) return false;
            if (!TryRequiredString(root, "icon", MaxIconLength, out var icon, out error)) return false;
            if (!IconRegex.IsMatch(icon))
            {
                error = "widget.json: 'icon' must match the pattern 'i-ph-<name>' or 'i-lucide-<name>'.";
                return false;
            }

            if (!TryParseEngine(root, out var engine, out error)) return false;
            if (!TryParseDefaultSize(root, out var defaultW, out var defaultH, out error)) return false;

            string? baseKind = null;
            string? configJson = null;
            string? specJson = null;

            switch (engine)
            {
                case WidgetEngine.Preset:
                    if (!TryRequiredString(root, "baseKind", MaxBaseKindLength, out var bk, out error)) return false;
                    if (bk.Contains(':', StringComparison.Ordinal))
                    {
                        error = "widget.json: 'baseKind' must be a builtin kind, no ':' prefix allowed.";
                        return false;
                    }
                    if (!BuiltinKinds.Contains(bk))
                    {
                        error = $"widget.json: 'baseKind' '{bk}' is not a recognized builtin kind.";
                        return false;
                    }
                    baseKind = bk;

                    if (!TryParseOpaqueObject(root, "defaultConfig", MaxConfigBytes, required: false, out configJson, out error))
                    {
                        return false;
                    }
                    if (configJson is null)
                    {
                        // Preset without config is degenerate but legal — the SPA's
                        // form will populate at first edit. Default to {}.
                        configJson = "{}";
                    }
                    break;

                case WidgetEngine.Spec:
                case WidgetEngine.Composite:
                    if (!TryParseOpaqueObject(root, "spec", MaxSpecBytes, required: true, out specJson, out error))
                    {
                        return false;
                    }
                    if (!TryParseOpaqueObject(root, "defaultConfig", MaxConfigBytes, required: false, out configJson, out error))
                    {
                        return false;
                    }
                    break;
            }

            // Optional `parameters` array shared by both engines: lets a widget
            // declare typed inputs (service_name, string, …) that the SPA renders
            // at the top of the config form and substitutes into `${param}`
            // placeholders inside the default metric binding. Server stays
            // opaque on the schema — the SPA owns validation.
            if (!TryParseOpaqueArray(root, "parameters", MaxConfigBytes, required: false, out var parametersJson, out error))
            {
                return false;
            }

            widget = new LibraryWidget
            {
                KindId = kindId,
                Name = name,
                Description = description,
                Icon = icon,
                Engine = engine,
                BaseKind = baseKind,
                ConfigJson = configJson,
                SpecJson = specJson,
                ParametersJson = parametersJson,
                DefaultW = defaultW,
                DefaultH = defaultH
            };
            error = null;
            return true;
        }
    }

    private static bool TryRequiredString(
        JsonElement root,
        string field,
        int maxLength,
        [NotNullWhen(true)] out string? value,
        [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!root.TryGetProperty(field, out var element) || element.ValueKind != JsonValueKind.String)
        {
            error = $"'{field}' is required and must be a string.";
            return false;
        }
        var s = element.GetString();
        if (string.IsNullOrWhiteSpace(s))
        {
            error = $"'{field}' must not be empty.";
            return false;
        }
        if (s.Length > maxLength)
        {
            error = $"'{field}' must be at most {maxLength} characters.";
            return false;
        }
        value = s;
        error = null;
        return true;
    }

    private static bool TryOptionalString(
        JsonElement root,
        string field,
        int maxLength,
        out string? value,
        [NotNullWhen(false)] out string? error)
    {
        value = null;
        if (!root.TryGetProperty(field, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            error = null;
            return true;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"'{field}' must be a string when present.";
            return false;
        }
        var s = element.GetString();
        if (s is not null && s.Length > maxLength)
        {
            error = $"'{field}' must be at most {maxLength} characters.";
            return false;
        }
        value = s;
        error = null;
        return true;
    }

    private static bool TryParseEngine(
        JsonElement root,
        out WidgetEngine engine,
        [NotNullWhen(false)] out string? error)
    {
        engine = WidgetEngine.Preset;
        if (!root.TryGetProperty("engine", out var element) || element.ValueKind != JsonValueKind.String)
        {
            error = "'engine' is required and must be one of 'preset', 'spec', 'composite'.";
            return false;
        }
        var raw = element.GetString();
        if (!Enum.TryParse<WidgetEngine>(raw, ignoreCase: true, out engine) || !Enum.IsDefined(engine))
        {
            error = $"'engine' must be one of 'preset', 'spec', 'composite' (got '{raw}').";
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryParseDefaultSize(
        JsonElement root,
        out int w,
        out int h,
        [NotNullWhen(false)] out string? error)
    {
        w = 3;
        h = 3;
        if (!root.TryGetProperty("defaultSize", out var size))
        {
            // Allow omission — defaults are sane.
            error = null;
            return true;
        }
        if (size.ValueKind != JsonValueKind.Object)
        {
            error = "'defaultSize' must be an object with 'w' and 'h'.";
            return false;
        }
        if (!size.TryGetProperty("w", out var wEl) || wEl.ValueKind != JsonValueKind.Number || !wEl.TryGetInt32(out w))
        {
            error = "'defaultSize.w' must be an integer.";
            return false;
        }
        if (!size.TryGetProperty("h", out var hEl) || hEl.ValueKind != JsonValueKind.Number || !hEl.TryGetInt32(out h))
        {
            error = "'defaultSize.h' must be an integer.";
            return false;
        }
        if (w is < 1 or > 12)
        {
            error = "'defaultSize.w' must be between 1 and 12.";
            return false;
        }
        if (h is < 1 or > 24)
        {
            error = "'defaultSize.h' must be between 1 and 24.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool TryParseOpaqueObject(
        JsonElement root,
        string field,
        int maxBytes,
        bool required,
        out string? rawJson,
        [NotNullWhen(false)] out string? error)
    {
        rawJson = null;
        if (!root.TryGetProperty(field, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                error = $"'{field}' is required and must be a JSON object.";
                return false;
            }
            error = null;
            return true;
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"'{field}' must be a JSON object.";
            return false;
        }
        var raw = element.GetRawText();
        var bytes = Encoding.UTF8.GetByteCount(raw);
        if (bytes > maxBytes)
        {
            error = $"'{field}' is too large ({bytes} bytes); maximum is {maxBytes} bytes.";
            return false;
        }
        rawJson = raw;
        error = null;
        return true;
    }

    private static bool TryParseOpaqueArray(
        JsonElement root,
        string field,
        int maxBytes,
        bool required,
        out string? rawJson,
        [NotNullWhen(false)] out string? error)
    {
        rawJson = null;
        if (!root.TryGetProperty(field, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                error = $"'{field}' is required and must be a JSON array.";
                return false;
            }
            error = null;
            return true;
        }
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"'{field}' must be a JSON array.";
            return false;
        }
        var raw = element.GetRawText();
        var bytes = Encoding.UTF8.GetByteCount(raw);
        if (bytes > maxBytes)
        {
            error = $"'{field}' is too large ({bytes} bytes); maximum is {maxBytes} bytes.";
            return false;
        }
        rawJson = raw;
        error = null;
        return true;
    }
}
