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
        TelemetryDbFunctions.RegisterJsonAttributeEquals(modelBuilder, Translate);
    }

    private static SqlBinaryExpression Translate(IReadOnlyList<SqlExpression> args)
    {
        var stringMapping = args[2].TypeMapping;
        var path = ConcatPath(args[1], stringMapping);
        var extract = new SqlFunctionExpression(
            functionName: "json_extract",
            arguments: [args[0], path],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            type: typeof(string),
            typeMapping: stringMapping);
        var asText = new SqlUnaryExpression(
            operatorType: ExpressionType.Convert,
            operand: extract,
            type: typeof(string),
            typeMapping: stringMapping);
        return new SqlBinaryExpression(
            ExpressionType.Equal,
            asText,
            args[2],
            typeof(bool),
            typeMapping: null);
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
