using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Converters;

/// <summary>
/// Serializes an attribute map to a UTF-8 JSON string column. Provider-agnostic:
/// the column type is plain TEXT / NVARCHAR / VARCHAR depending on the provider,
/// but the payload is identical everywhere.
/// </summary>
public sealed class AttributesJsonConverter : ValueConverter<IReadOnlyDictionary<string, object?>, string>
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public AttributesJsonConverter()
        : base(
            attrs => Serialize(attrs),
            json => Deserialize(json))
    {
    }

    // Reference-equality comparer + identity snapshot. The previous JSON-based
    // comparer round-tripped every attribute map through Serialize+Deserialize
    // on the change-tracker hot path (one Snapshot per tracked entity per
    // SaveChanges) — a pure waste because the sinks only Add entities (never
    // mutate them in place). Sinks that take the Add-only path also flip
    // ChangeTracker.AutoDetectChangesEnabled = false so EF skips DetectChanges
    // entirely.
    public static readonly ValueComparer<IReadOnlyDictionary<string, object?>> Comparer =
        new(
            (a, b) => ReferenceEquals(a, b),
            d => RuntimeHelpers.GetHashCode(d),
            d => d);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = false,
        };
        options.Converters.Add(new ObjectJsonConverter());
        return options;
    }

    private static string Serialize(IReadOnlyDictionary<string, object?>? value) =>
        value is null || value.Count == 0
            ? "{}"
            : JsonSerializer.Serialize(value, Options);

    private static IReadOnlyDictionary<string, object?> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return AttributeMap.Empty;
        }

        var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, Options);
        return parsed ?? AttributeMap.Empty;
    }
}
