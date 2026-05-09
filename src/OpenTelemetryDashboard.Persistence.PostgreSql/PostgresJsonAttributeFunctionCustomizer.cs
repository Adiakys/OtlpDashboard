using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenTelemetryDashboard.Persistence.PostgreSql;

/// <summary>
/// Wires <see cref="TelemetryDbFunctions.JsonAttributeEquals"/> to
/// PostgreSQL's <c>jsonb_extract_path_text</c> — returns text for any
/// JSON value type, so the equality compare matches strings, numbers
/// and booleans uniformly. Plugged in by
/// <see cref="PostgreSqlTelemetryStoreExtensions"/> via
/// <c>ReplaceService&lt;IModelCustomizer&gt;</c>. The text → jsonb
/// cast lives here (not in the shared persistence project) because
/// it needs Npgsql's jsonb type mapping.
/// </summary>
internal sealed class PostgresJsonAttributeFunctionCustomizer : RelationalModelCustomizer
{
    private readonly IRelationalTypeMappingSource _typeMappingSource;

    public PostgresJsonAttributeFunctionCustomizer(
        ModelCustomizerDependencies dependencies,
        IRelationalTypeMappingSource typeMappingSource)
        : base(dependencies)
    {
        _typeMappingSource = typeMappingSource;
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        base.Customize(modelBuilder, context);

        var jsonbMapping = _typeMappingSource.FindMapping("jsonb");
        if (jsonbMapping is null) return;
        TelemetryDbFunctions.RegisterJsonAttributeEquals(modelBuilder, args => TranslateEquals(args, jsonbMapping));
        TelemetryDbFunctions.RegisterJsonAttributeValue(modelBuilder, args => TranslateValue(args, jsonbMapping));
    }

    private static SqlBinaryExpression TranslateEquals(
        IReadOnlyList<SqlExpression> args,
        RelationalTypeMapping jsonbMapping)
    {
        var extract = BuildExtract(args[0], args[1], jsonbMapping, args[2].TypeMapping);
        return new SqlBinaryExpression(
            ExpressionType.Equal,
            extract,
            args[2],
            typeof(bool),
            typeMapping: null);
    }

    private static SqlFunctionExpression TranslateValue(
        IReadOnlyList<SqlExpression> args,
        RelationalTypeMapping jsonbMapping)
    {
        return BuildExtract(args[0], args[1], jsonbMapping, args[1].TypeMapping);
    }

    private static SqlFunctionExpression BuildExtract(
        SqlExpression jsonColumn,
        SqlExpression key,
        RelationalTypeMapping jsonbMapping,
        RelationalTypeMapping? stringMapping)
    {
        var jsonbCast = new SqlUnaryExpression(
            operatorType: ExpressionType.Convert,
            operand: jsonColumn,
            type: typeof(string),
            typeMapping: jsonbMapping);
        // argumentsPropagateNullability: false on both — jsonb_extract_path_text
        // returns NULL when the key is absent regardless of whether the
        // arguments are non-null. Leaving propagation as `true` makes EF
        // collapse `... IS NULL` predicates against the function's
        // result to `WHERE 0` (the JSON column and the key parameter
        // are themselves NOT NULL), which breaks the per-span attribute
        // priority pick in EfCoreServiceMapReader.
        return new SqlFunctionExpression(
            functionName: "jsonb_extract_path_text",
            arguments: [jsonbCast, key],
            nullable: true,
            argumentsPropagateNullability: [false, false],
            type: typeof(string),
            typeMapping: stringMapping);
    }
}
