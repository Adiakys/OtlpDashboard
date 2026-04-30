using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Data-only migration that prefixes existing builtin widget kinds with
    /// <c>std:</c> so they line up with the SPA's fully-qualified kind
    /// scheme (<c>std:metric-stat</c>, <c>custom:&lt;guid&gt;</c>,
    /// <c>library:&lt;libId&gt;/&lt;kindId&gt;</c>). Idempotent: any kind
    /// already containing a colon is skipped.
    /// </summary>
    public partial class NormalizeWidgetKindsToFqn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE dashboard_widgets SET kind = 'std:' || kind WHERE kind NOT LIKE '%:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort revert: strip the std: prefix. Only valid on a
            // database that never persisted a non-std widget; custom and
            // library kinds would lose source attribution.
            migrationBuilder.Sql(
                "UPDATE dashboard_widgets SET kind = substr(kind, 5) WHERE kind LIKE 'std:%';");
        }
    }
}
