using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using OpenTelemetryDashboard.Dashboards.Contracts;

namespace OpenTelemetryDashboard.Dashboards.Validation;

/// <summary>
/// Imperative validators for dashboard write operations. Pattern aligned with
/// <c>QueryValidation</c> in the Api module: returns RFC 7807-shaped error
/// dictionaries on failure, leaves semantic widget validation to the client
/// (which owns the layout schema).
/// </summary>
public static class DashboardValidation
{
    /// <summary>
    /// Maximum size of the serialized layout document (256 KB). Anything
    /// larger almost certainly indicates a runaway client serialization bug.
    /// </summary>
    public const int MaxLayoutJsonBytes = 256 * 1024;

    /// <summary>Maximum length of <see cref="SaveDashboardRequest.Name"/>.</summary>
    public const int MaxNameLength = 200;

    public static bool TryValidateSave(
        SaveDashboardRequest request,
        [NotNullWhen(false)] out Dictionary<string, string[]>? errors)
    {
        ArgumentNullException.ThrowIfNull(request);

        var problems = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            problems["name"] = ["The 'name' field is required."];
        }
        else if (request.Name.Length > MaxNameLength)
        {
            problems["name"] = [$"'name' must be at most {MaxNameLength} characters."];
        }

        if (string.IsNullOrEmpty(request.LayoutJson))
        {
            problems["layoutJson"] = ["The 'layoutJson' field is required."];
        }
        else
        {
            // Reject byte length, not character length: text columns are
            // sized in bytes downstream and the JSON wire payload is UTF-8.
            var byteLength = System.Text.Encoding.UTF8.GetByteCount(request.LayoutJson);
            if (byteLength > MaxLayoutJsonBytes)
            {
                problems["layoutJson"] = [
                    $"'layoutJson' is too large ({byteLength} bytes); maximum is {MaxLayoutJsonBytes} bytes."
                ];
            }
            else if (!IsValidLayoutDocument(request.LayoutJson))
            {
                problems["layoutJson"] = [
                    "'layoutJson' must be a JSON object with a 'widgets' array."
                ];
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

    private static bool IsValidLayoutDocument(string layoutJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(layoutJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("widgets", out var widgets))
            {
                return false;
            }

            return widgets.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
