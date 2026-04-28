using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace OpenTelemetryDashboard.Persistence.Naming;

/// <summary>
/// Applies <c>snake_case</c> naming to tables, columns, keys, indexes, and
/// foreign-key constraints. Provider-agnostic: identifiers remain valid on
/// SQLite, PostgreSQL, SQL Server, and MySQL.
/// </summary>
public static class SnakeCaseNamingExtensions
{
    public static void ApplySnakeCaseNaming(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (var property in entity.GetProperties())
            {
                var columnName = property.GetColumnName();
                if (columnName is not null)
                {
                    property.SetColumnName(ToSnakeCase(columnName));
                }
            }

            foreach (var key in entity.GetKeys())
            {
                var keyName = key.GetName();
                if (keyName is not null)
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                var constraintName = fk.GetConstraintName();
                if (constraintName is not null)
                {
                    fk.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (var index in entity.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (indexName is not null)
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }
    }

    private static string ToSnakeCase(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier;
        }

        var builder = new StringBuilder(identifier.Length + 8);
        for (var i = 0; i < identifier.Length; i++)
        {
            var current = identifier[i];
            var previous = i > 0 ? identifier[i - 1] : '\0';
            var next = i + 1 < identifier.Length ? identifier[i + 1] : '\0';

            if (char.IsUpper(current) && i > 0 &&
                (char.IsLower(previous) || (char.IsUpper(previous) && char.IsLower(next))))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLower(current, CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
