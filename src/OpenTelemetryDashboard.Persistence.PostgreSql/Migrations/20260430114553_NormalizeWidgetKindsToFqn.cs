using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.PostgreSql.Migrations
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
            migrationBuilder.Sql(
                "UPDATE dashboard_widgets SET kind = 'std:' || kind WHERE kind NOT LIKE '%:%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE dashboard_widgets SET kind = substring(kind from 5) WHERE kind LIKE 'std:%';");
        }
    }
}
