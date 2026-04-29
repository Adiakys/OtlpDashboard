using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Save payload for a dashboard (create or update). <see cref="RowVersion"/>
/// participates in optimistic concurrency: pass the value most recently
/// returned by the server.
/// </summary>
public sealed record SaveDashboardRequest(
    string Name,
    IReadOnlyList<DashboardWidgetDto> Widgets,
    uint RowVersion)
{
    /// <summary>Maximum length of <see cref="Name"/>. Mirrors the EF column
    /// constraint so validation rejects before SaveChanges.</summary>
    public const int MaxNameLength = 32;

    /// <summary>Sanity bound on widget count — way above any realistic
    /// layout — protects the store from runaway clients.</summary>
    public const int MaxWidgets = 100;

    /// <summary>Maximum length of <see cref="DashboardWidgetDto.Kind"/>.
    /// Mirrors the EF column constraint.</summary>
    public const int MaxKindLength = 64;

    /// <summary>Maximum size of one widget's serialized config (64 KB).</summary>
    public const int MaxConfigBytes = 64 * 1024;

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

        if (Widgets is null)
        {
            problems["widgets"] = ["The 'widgets' field is required."];
        }
        else if (Widgets.Count > MaxWidgets)
        {
            problems["widgets"] = [$"At most {MaxWidgets} widgets are allowed per dashboard."];
        }
        else
        {
            for (var i = 0; i < Widgets.Count; i++)
            {
                ValidateWidget(Widgets[i], i, problems);
            }
        }

        if (problems.Count > 0)
        {
            errors = problems;
            return false;
        }

        errors = null;
        return true;
    }

    private static void ValidateWidget(
        DashboardWidgetDto widget,
        int index,
        Dictionary<string, string[]> problems)
    {
        if (string.IsNullOrWhiteSpace(widget.Kind))
        {
            problems[$"widgets[{index}].kind"] = ["'kind' is required."];
        }
        else if (widget.Kind.Length > MaxKindLength)
        {
            problems[$"widgets[{index}].kind"] = [$"'kind' must be at most {MaxKindLength} characters."];
        }

        if (widget.X < 0)
        {
            problems[$"widgets[{index}].x"] = ["'x' must be >= 0."];
        }
        if (widget.Y < 0)
        {
            problems[$"widgets[{index}].y"] = ["'y' must be >= 0."];
        }
        if (widget.W < 1)
        {
            problems[$"widgets[{index}].w"] = ["'w' must be >= 1."];
        }
        if (widget.H < 1)
        {
            problems[$"widgets[{index}].h"] = ["'h' must be >= 1."];
        }

        // Config must be a JSON object — null/array/primitives are rejected
        // so the per-kind shape can grow without ambiguity. Size cap guards
        // against runaway clients (the column itself is unbounded text).
        switch (widget.Config.ValueKind)
        {
            case JsonValueKind.Undefined:
                problems[$"widgets[{index}].config"] = ["'config' is required."];
                break;
            case JsonValueKind.Object:
                var bytes = Encoding.UTF8.GetByteCount(widget.Config.GetRawText());
                if (bytes > MaxConfigBytes)
                {
                    problems[$"widgets[{index}].config"] =
                        [$"'config' is too large ({bytes} bytes); maximum is {MaxConfigBytes} bytes."];
                }
                break;
            default:
                problems[$"widgets[{index}].config"] = ["'config' must be a JSON object."];
                break;
        }
    }
}
