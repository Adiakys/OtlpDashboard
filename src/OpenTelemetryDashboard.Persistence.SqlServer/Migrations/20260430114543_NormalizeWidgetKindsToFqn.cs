using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Data-only migration that prefixes existing builtin widget kinds with
    /// <c>std:</c> so they line up with the SPA's fully-qualified kind
    /// scheme. Idempotent: any kind already containing a colon is skipped.
    /// </summary>
    public partial class NormalizeWidgetKindsToFqn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server uses + for string concatenation.
            migrationBuilder.Sql(
                "UPDATE dashboard_widgets SET kind = 'std:' + kind WHERE kind NOT LIKE '%:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE dashboard_widgets SET kind = SUBSTRING(kind, 5, LEN(kind)) WHERE kind LIKE 'std:%';");
        }
    }
}
