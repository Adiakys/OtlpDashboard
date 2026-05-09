using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenTelemetryDashboard.Persistence.Sqlite;

/// <summary>
/// Wires <see cref="TelemetryDbFunctions.JsonAttributeEquals"/> to the
/// SQLite native <c>json_extract</c>. <c>json_extract</c> returns the
/// JSON value's storage class (INTEGER for numbers, TEXT for strings),
/// so the result is cast to TEXT before the equality compare —
/// otherwise <c>200 = '200'</c> evaluates false because INTEGER ≠ TEXT.
/// Plugged in by <see cref="SqliteTelemetryStoreExtensions"/> via
/// <c>ReplaceService&lt;IModelCustomizer&gt;</c>.
/// </summary>
internal sealed class SqliteJsonAttributeFunctionCustomizer : RelationalModelCustomizer
{
    public SqliteJsonAttributeFunctionCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        base.Customize(modelBuilder, context);
        TelemetryDbFunctions.RegisterJsonAttributeEquals(modelBuilder, TranslateEquals);
        TelemetryDbFunctions.RegisterJsonAttributeValue(modelBuilder, TranslateValue);
    }

    private static SqlBinaryExpression TranslateEquals(IReadOnlyList<SqlExpression> args)
    {
        var stringMapping = args[2].TypeMapping;
        var asText = BuildExtractAsText(args[0], args[1], stringMapping);
        return new SqlBinaryExpression(
            ExpressionType.Equal,
            asText,
            args[2],
            typeof(bool),
            typeMapping: null);
    }

    /// <summary>Returns <c>json_extract(json, '$."<key>"')</c> as a
    /// nullable string. The equality translation in
    /// <see cref="TranslateEquals"/> wraps json_extract in a CAST so
    /// SQLite's type-affinity rules don't fail
    /// <c>200 = '200'</c>; here we don't apply the cast because we'd
    /// lose IS NULL semantics: the EF nullability tracker sees
    /// <c>SqlUnaryExpression(Convert)</c> as non-nullable, then folds
    /// every <c>... IS NULL</c> predicate to <c>WHERE 0</c>.
    /// <para>
    /// <c>argumentsPropagateNullability</c> is <see langword="false"/>
    /// for both arguments because the function's null-ness is
    /// <i>independent</i> of the inputs — json_extract returns NULL
    /// when the key is absent, even if both arguments are non-null. If
    /// we left this <see langword="true"/>, EF would conclude that
    /// since the JSON column and the path are NOT NULL the result is
    /// NOT NULL too, and would still fold <c>... IS NULL</c> to
    /// <c>WHERE 0</c> — the bug we just removed via the missing CAST
    /// would come back through a different door.
    /// </para>
    /// </summary>
    private static SqlFunctionExpression TranslateValue(IReadOnlyList<SqlExpression> args)
    {
        var stringMapping = args[1].TypeMapping;
        var path = ConcatPath(args[1], stringMapping);
        return new SqlFunctionExpression(
            functionName: "json_extract",
            arguments: [args[0], path],
            nullable: true,
            argumentsPropagateNullability: [false, false],
            type: typeof(string),
            typeMapping: stringMapping);
    }

    private static SqlUnaryExpression BuildExtractAsText(
        SqlExpression jsonColumn, SqlExpression key, RelationalTypeMapping? stringMapping)
    {
        var path = ConcatPath(key, stringMapping);
        var extract = new SqlFunctionExpression(
            functionName: "json_extract",
            arguments: [jsonColumn, path],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            type: typeof(string),
            typeMapping: stringMapping);
        return new SqlUnaryExpression(
            operatorType: ExpressionType.Convert,
            operand: extract,
            type: typeof(string),
            typeMapping: stringMapping);
    }

    /// <summary>Build the path SQL <c>'$."' || key || '"'</c>. The key
    /// is parameterised, so multiple distinct keys reuse the same query
    /// plan.</summary>
    private static SqlBinaryExpression ConcatPath(SqlExpression key, RelationalTypeMapping? mapping)
    {
        var prefix = new SqlConstantExpression("$.\"", mapping);
        var suffix = new SqlConstantExpression("\"", mapping);
        return new SqlBinaryExpression(
            ExpressionType.Add,
            new SqlBinaryExpression(ExpressionType.Add, prefix, key, typeof(string), mapping),
            suffix,
            typeof(string),
            mapping);
    }
}
