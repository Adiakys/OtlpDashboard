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
        TelemetryDbFunctions.RegisterJsonAttributeEquals(modelBuilder, Translate);
    }

    private static SqlBinaryExpression Translate(IReadOnlyList<SqlExpression> args)
    {
        var stringMapping = args[2].TypeMapping;
        var path = ConcatPath(args[1], stringMapping);
        var extract = new SqlFunctionExpression(
            functionName: "JSON_VALUE",
            arguments: [args[0], path],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            type: typeof(string),
            typeMapping: stringMapping);
        return new SqlBinaryExpression(
            ExpressionType.Equal,
            extract,
            args[2],
            typeof(bool),
            typeMapping: null);
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
