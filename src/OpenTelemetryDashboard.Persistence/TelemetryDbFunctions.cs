using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace OpenTelemetryDashboard.Persistence;

/// <summary>
/// Server-side-only EF DB functions. Direct calls throw — the methods
/// exist only to anchor per-provider translations registered by each
/// storage provider's package (<c>Persistence.Sqlite</c>,
/// <c>Persistence.SqlServer</c>, <c>Persistence.PostgreSql</c>) via
/// <see cref="RegisterJsonAttributeEquals"/>.
/// </summary>
public static class TelemetryDbFunctions
{
    /// <summary>
    /// Match a single attribute pair against the JSON column. Used by
    /// the log/trace readers to translate <c>?attr=key:value</c> into a
    /// provider-native JSON path predicate (<c>json_extract</c> on
    /// SQLite, <c>JSON_VALUE</c> on SqlServer,
    /// <c>jsonb_extract_path_text</c> on PostgreSQL).
    /// </summary>
    public static bool JsonAttributeEquals(string json, string key, string value) =>
        throw new InvalidOperationException(
            $"{nameof(JsonAttributeEquals)} is server-side only — use it inside an EF query.");

    private static readonly MethodInfo JsonAttributeEqualsMethod = typeof(TelemetryDbFunctions)
        .GetMethod(nameof(JsonAttributeEquals), BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            $"{nameof(JsonAttributeEquals)} not found — placeholder removed?");

    /// <summary>
    /// Hook a provider's translation onto <see cref="JsonAttributeEquals"/>.
    /// The <paramref name="translation"/> receives the three call args
    /// (column ref, key, value) and returns the boolean SQL predicate.
    /// </summary>
    public static void RegisterJsonAttributeEquals(
        ModelBuilder modelBuilder,
        Func<IReadOnlyList<SqlExpression>, SqlExpression> translation)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(translation);
        modelBuilder.HasDbFunction(JsonAttributeEqualsMethod).HasTranslation(translation);
    }
}
