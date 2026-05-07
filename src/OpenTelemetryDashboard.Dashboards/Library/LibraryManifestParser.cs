using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Strict JSON parser for the three on-disk manifests that make up a pack:
/// <c>pack.json</c> at the pack root, <c>manifest.json</c> at each library
/// root, and per-widget <c>widget.json</c>. The parser never executes user
/// code and never trusts inputs blindly: every string is bounded, every
/// payload is size-capped, and unknown shapes are rejected with a
/// human-readable diagnostic the registry surfaces in the logs.
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
    private const int MaxHomepageLength = 256;
    private const int MaxRelativePathLength = 256;
    private const int MaxLibrariesPerPack = 64;
    private const int MaxDashboardsPerPack = 64;

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
    /// Parsed <c>pack.json</c>. Pack metadata + the fixed asset slots
    /// (libraries, dashboards). Future asset types arrive as new top-level
    /// arrays — we deliberately don't expose a generic dispatcher here
    /// because typed slots make the schema self-documenting.
    /// </summary>
    public sealed record PackManifest(
        string Id,
        string Name,
        string Version,
        string? Author,
        string? License,
        string? Description,
        string? Homepage,
        IReadOnlyList<PackLibraryRef> Libraries,
        IReadOnlyList<PackDashboardRef> Dashboards);

    /// <summary>Library entry inside a <see cref="PackManifest"/>.
    /// <see cref="Id"/> matches the in-pack directory's manifest id;
    /// <see cref="RelativePath"/> is forward-slash relative, validated
    /// to stay inside the pack root.</summary>
    public sealed record PackLibraryRef(string Id, string RelativePath);

    /// <summary>Dashboard entry inside a <see cref="PackManifest"/>.
    /// <see cref="Builtin"/> requests the seeder treat the dashboard
    /// as a built-in (idempotent first-boot insert + pristine-upsert
    /// for the default dashboard). Without it the dashboard is just an
    /// installable template.</summary>
    public sealed record PackDashboardRef(string Id, string RelativePath, bool Builtin);

    /// <summary>
    /// Library-level header. <see cref="Icon"/> drives the picker
    /// section glyph; pack-level metadata (version, author, license)
    /// lives in <see cref="PackManifest"/> instead — a library inside a
    /// pack inherits those concerns from its parent.
    /// </summary>
    public sealed record ManifestHeader(
        string Id,
        string Name,
        string? Description,
        string? Icon);

    /// <summary>
    /// Parse a <c>pack.json</c> payload. <paramref name="expectedId"/> is the
    /// on-disk pack directory name — same id-must-match-folder rule as
    /// libraries, so the value can never be used to redirect the loader to
    /// an arbitrary path. Library / dashboard relative paths are validated
    /// against <c>..</c>, absolute prefixes, drive letters; the caller still
    /// re-checks the resolved absolute path against the pack root before
    /// touching the filesystem.
    /// </summary>
    public static bool TryParsePack(
        string json,
        string expectedId,
        [NotNullWhen(true)] out PackManifest? pack,
        [NotNullWhen(false)] out string? error)
    {
        pack = null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"pack.json is not valid JSON: {ex.Message}";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "pack.json must be a JSON object.";
                return false;
            }

            if (!TryRequiredString(root, "id", MaxIdLength, out var id, out error)) return false;
            if (!IdRegex.IsMatch(id))
            {
                error = "pack.json: 'id' must match [a-z0-9-] (lowercase, no slashes, no dots).";
                return false;
            }
            if (!string.Equals(id, expectedId, StringComparison.Ordinal))
            {
                error = $"pack.json: 'id' ('{id}') does not match the directory name ('{expectedId}').";
                return false;
            }

            if (!TryRequiredString(root, "name", MaxNameLength, out var name, out error)) return false;
            if (!TryRequiredString(root, "version", MaxVersionLength, out var version, out error)) return false;
            if (!TryOptionalString(root, "author", MaxAuthorLength, out var author, out error)) return false;
            if (!TryOptionalString(root, "license", MaxLicenseLength, out var license, out error)) return false;
            if (!TryOptionalString(root, "description", MaxDescriptionLength, out var description, out error)) return false;
            if (!TryOptionalString(root, "homepage", MaxHomepageLength, out var homepage, out error)) return false;
            if (homepage is not null && !IsHttpsUrl(homepage))
            {
                error = "pack.json: 'homepage' must be an https URL.";
                return false;
            }

            if (!TryParseLibraryRefs(root, out var libs, out error)) return false;
            if (!TryParseDashboardRefs(root, out var dashes, out error)) return false;

            if (libs.Count == 0 && dashes.Count == 0)
            {
                error = "pack.json: a pack must declare at least one entry under 'libraries' or 'dashboards'.";
                return false;
            }

            pack = new PackManifest(id, name, version, author, license, description, homepage, libs, dashes);
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Parse a library <c>manifest.json</c>. <paramref name="expectedId"/> is
    /// the on-disk directory name; the parser enforces that <c>id</c>
    /// matches it before the value is used as a path component anywhere.
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
            if (!TryOptionalString(root, "description", MaxDescriptionLength, out var description, out error)) return false;
            if (!TryOptionalString(root, "icon", MaxIconLength, out var icon, out error)) return false;
            if (icon is not null && !IconRegex.IsMatch(icon))
            {
                error = "manifest.json: 'icon' must match the pattern 'i-ph-<name>' or 'i-lucide-<name>'.";
                return false;
            }

            header = new ManifestHeader(id, name, description, icon);
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

    private static bool TryParseLibraryRefs(
        JsonElement root,
        out IReadOnlyList<PackLibraryRef> refs,
        [NotNullWhen(false)] out string? error)
    {
        refs = [];
        if (!root.TryGetProperty("libraries", out var arr))
        {
            error = null;
            return true;
        }
        if (arr.ValueKind == JsonValueKind.Null)
        {
            error = null;
            return true;
        }
        if (arr.ValueKind != JsonValueKind.Array)
        {
            error = "pack.json: 'libraries' must be a JSON array.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<PackLibraryRef>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (list.Count >= MaxLibrariesPerPack)
            {
                error = $"pack.json: 'libraries' may declare at most {MaxLibrariesPerPack} entries.";
                return false;
            }
            if (entry.ValueKind != JsonValueKind.Object)
            {
                error = "pack.json: each entry in 'libraries' must be an object.";
                return false;
            }
            if (!TryRequiredString(entry, "id", MaxIdLength, out var libId, out error)) return false;
            if (!IdRegex.IsMatch(libId))
            {
                error = $"pack.json: library 'id' '{libId}' must match [a-z0-9-].";
                return false;
            }
            if (!seen.Add(libId))
            {
                error = $"pack.json: duplicate library id '{libId}'.";
                return false;
            }
            if (!TryRequiredString(entry, "path", MaxRelativePathLength, out var path, out error)) return false;
            if (!IsSafeRelativePath(path))
            {
                error = $"pack.json: library '{libId}' has an unsafe path '{path}'.";
                return false;
            }
            list.Add(new PackLibraryRef(libId, path));
        }

        refs = list;
        error = null;
        return true;
    }

    private static bool TryParseDashboardRefs(
        JsonElement root,
        out IReadOnlyList<PackDashboardRef> refs,
        [NotNullWhen(false)] out string? error)
    {
        refs = [];
        if (!root.TryGetProperty("dashboards", out var arr))
        {
            error = null;
            return true;
        }
        if (arr.ValueKind == JsonValueKind.Null)
        {
            error = null;
            return true;
        }
        if (arr.ValueKind != JsonValueKind.Array)
        {
            error = "pack.json: 'dashboards' must be a JSON array.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<PackDashboardRef>();
        foreach (var entry in arr.EnumerateArray())
        {
            if (list.Count >= MaxDashboardsPerPack)
            {
                error = $"pack.json: 'dashboards' may declare at most {MaxDashboardsPerPack} entries.";
                return false;
            }
            if (entry.ValueKind != JsonValueKind.Object)
            {
                error = "pack.json: each entry in 'dashboards' must be an object.";
                return false;
            }
            if (!TryRequiredString(entry, "id", MaxIdLength, out var dashId, out error)) return false;
            if (!IdRegex.IsMatch(dashId))
            {
                error = $"pack.json: dashboard 'id' '{dashId}' must match [a-z0-9-].";
                return false;
            }
            if (!seen.Add(dashId))
            {
                error = $"pack.json: duplicate dashboard id '{dashId}'.";
                return false;
            }
            if (!TryRequiredString(entry, "path", MaxRelativePathLength, out var path, out error)) return false;
            if (!IsSafeRelativePath(path))
            {
                error = $"pack.json: dashboard '{dashId}' has an unsafe path '{path}'.";
                return false;
            }

            var builtin = false;
            if (entry.TryGetProperty("builtin", out var bEl) && bEl.ValueKind != JsonValueKind.Null)
            {
                if (bEl.ValueKind != JsonValueKind.True && bEl.ValueKind != JsonValueKind.False)
                {
                    error = $"pack.json: dashboard '{dashId}' has 'builtin' that is not a boolean.";
                    return false;
                }
                builtin = bEl.GetBoolean();
            }

            list.Add(new PackDashboardRef(dashId, path, builtin));
        }

        refs = list;
        error = null;
        return true;
    }

    /// <summary>
    /// Validate a pack-declared relative path: forward-slash separated, no
    /// scheme, no drive letter, no absolute prefix, no <c>..</c> segment.
    /// The caller still re-resolves and confirms containment within the
    /// pack root before touching the filesystem — this is the cheap first
    /// line of defence the parser provides.
    /// </summary>
    private static bool IsSafeRelativePath(string path)
    {
        if (path.Length == 0) return false;
        if (path[0] is '/' or '\\') return false;
        if (path.Contains(':', StringComparison.Ordinal)) return false;
        if (path.Contains('\\', StringComparison.Ordinal)) return false;
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0) return false;
            if (segment is "." or "..") return false;
        }
        return true;
    }

    private static bool IsHttpsUrl(string raw) =>
        Uri.TryCreate(raw, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

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
