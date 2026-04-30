using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Save payload for a custom widget definition (create or update).
/// <see cref="RowVersion"/> participates in optimistic concurrency.
/// </summary>
public sealed record SaveWidgetDefinitionRequest(
    string Name,
    string? Description,
    string Icon,
    WidgetEngine Engine,
    string? BaseKind,
    JsonElement Config,
    JsonElement? Spec,
    int DefaultW,
    int DefaultH,
    uint RowVersion)
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 280;
    public const int MaxIconLength = 64;
    public const int MaxBaseKindLength = 64;

    /// <summary>Per-widget config payload size cap (64 KiB).</summary>
    public const int MaxConfigBytes = 64 * 1024;

    /// <summary>Spec payload size cap (256 KiB) — Vega-Lite specs can be
    /// chunky once data is embedded; we don't embed user data here, so
    /// 256 KiB is comfortable headroom.</summary>
    public const int MaxSpecBytes = 256 * 1024;

    public const int MinDefaultW = 1;
    public const int MaxDefaultW = 12;
    public const int MinDefaultH = 1;
    public const int MaxDefaultH = 24;

    /// <summary>
    /// Icons must look like <c>i-ph-foo-bar</c> or <c>i-lucide-foo</c>. Any
    /// other class is rejected to prevent arbitrary CSS landing in the
    /// rendered DOM.
    /// </summary>
    private static readonly Regex IconRegex = new(
        @"^i-(ph|lucide)-[a-z0-9]+(-[a-z0-9]+)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool TryValidateRequest([NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        var problems = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(Name))
        {
            problems["name"] = ["The 'name' field is required."];
        }
        else if (Name.Length > MaxNameLength)
        {
            problems["name"] = [$"'name' must be at most {MaxNameLength} characters."];
        }

        if (Description is not null && Description.Length > MaxDescriptionLength)
        {
            problems["description"] =
                [$"'description' must be at most {MaxDescriptionLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(Icon))
        {
            problems["icon"] = ["The 'icon' field is required."];
        }
        else if (Icon.Length > MaxIconLength || !IconRegex.IsMatch(Icon))
        {
            problems["icon"] =
                ["'icon' must match the pattern 'i-ph-<name>' or 'i-lucide-<name>'."];
        }

        if (!Enum.IsDefined(Engine))
        {
            problems["engine"] = ["'engine' is invalid."];
        }

        switch (Engine)
        {
            case WidgetEngine.Preset:
                ValidateBaseKind(problems);
                break;
            case WidgetEngine.Spec:
            case WidgetEngine.Composite:
                ValidateSpec(problems);
                break;
        }

        ValidateConfig(problems);

        if (DefaultW is < MinDefaultW or > MaxDefaultW)
        {
            problems["defaultW"] = [$"'defaultW' must be between {MinDefaultW} and {MaxDefaultW}."];
        }
        if (DefaultH is < MinDefaultH or > MaxDefaultH)
        {
            problems["defaultH"] = [$"'defaultH' must be between {MinDefaultH} and {MaxDefaultH}."];
        }

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        errors = null;
        return true;
    }

    private void ValidateBaseKind(Dictionary<string, string[]> problems)
    {
        if (string.IsNullOrWhiteSpace(BaseKind))
        {
            problems["baseKind"] = ["'baseKind' is required for engine 'preset'."];
            return;
        }

        if (BaseKind.Length > MaxBaseKindLength)
        {
            problems["baseKind"] =
                [$"'baseKind' must be at most {MaxBaseKindLength} characters."];
            return;
        }

        // baseKind references a builtin: never a custom or library kind, never
        // already-prefixed. Prevents recursive presets and accidental cycles.
        if (BaseKind.Contains(':', StringComparison.Ordinal))
        {
            problems["baseKind"] =
                ["'baseKind' must reference a builtin and cannot include ':' (no custom or library chains)."];
        }
    }

    private void ValidateSpec(Dictionary<string, string[]> problems)
    {
        if (Spec is null || Spec.Value.ValueKind != JsonValueKind.Object)
        {
            problems["spec"] = ["'spec' must be a JSON object for engine 'spec' or 'composite'."];
            return;
        }

        var bytes = Encoding.UTF8.GetByteCount(Spec.Value.GetRawText());
        if (bytes > MaxSpecBytes)
        {
            problems["spec"] = [$"'spec' is too large ({bytes} bytes); maximum is {MaxSpecBytes} bytes."];
        }
    }

    private void ValidateConfig(Dictionary<string, string[]> problems)
    {
        switch (Config.ValueKind)
        {
            case JsonValueKind.Undefined:
                problems["config"] = ["'config' is required."];
                break;
            case JsonValueKind.Object:
                var bytes = Encoding.UTF8.GetByteCount(Config.GetRawText());
                if (bytes > MaxConfigBytes)
                {
                    problems["config"] =
                        [$"'config' is too large ({bytes} bytes); maximum is {MaxConfigBytes} bytes."];
                }
                break;
            default:
                problems["config"] = ["'config' must be a JSON object."];
                break;
        }
    }
}
