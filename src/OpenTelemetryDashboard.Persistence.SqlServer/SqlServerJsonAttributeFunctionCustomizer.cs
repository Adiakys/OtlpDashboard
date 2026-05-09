using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace OpenTelemetryDashboard.Persistence.SqlServer;

/// <summary>
/// Wires <see cref="TelemetryDbFunctions.JsonAttributeEquals"/> to SQL
/// Server's <c>JSON_VALUE</c>, which always returns NVARCHAR — no
/// CAST needed for the equality compare. Plugged in by
/// <see cref="SqlServerTelemetryStoreExtensions"/> via
/// <c>ReplaceService&lt;IModelCustomizer&gt;</c>.
/// </summary>
internal sealed class SqlServerJsonAttributeFunctionCustomizer : RelationalModelCustomizer
{
    public SqlServerJsonAttributeFunctionCustomizer(ModelCustomizerDependencies dependencies)
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
        var extract = BuildJsonValue(args[0], args[1], stringMapping);
        return new SqlBinaryExpression(
            ExpressionType.Equal,
            extract,
            args[2],
            typeof(bool),
            typeMapping: null);
    }

    private static SqlFunctionExpression TranslateValue(IReadOnlyList<SqlExpression> args)
    {
        var stringMapping = args[1].TypeMapping;
        return BuildJsonValue(args[0], args[1], stringMapping);
    }

    private static SqlFunctionExpression BuildJsonValue(
        SqlExpression jsonColumn, SqlExpression key, RelationalTypeMapping? stringMapping)
    {
        var path = ConcatPath(key, stringMapping);
        // argumentsPropagateNullability is false: JSON_VALUE returns
        // NULL when the path doesn't match, independently of whether
        // the arguments are non-null. With propagation enabled EF would
        // simplify `... IS NULL` predicates to false (since the column
        // and path are non-nullable), breaking the priority-pick
        // synthesis in EfCoreServiceMapReader.
        return new SqlFunctionExpression(
            functionName: "JSON_VALUE",
            arguments: [jsonColumn, path],
            nullable: true,
            argumentsPropagateNullability: [false, false],
            type: typeof(string),
            typeMapping: stringMapping);
    }

    /// <summary>SQL Server uses the same JSON path syntax as SQLite —
    /// <c>'$."<key>"'</c> — so the path concat helper is shared in shape
    /// but kept local to each provider so each translation owns the SQL
    /// fragment end-to-end.</summary>
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
