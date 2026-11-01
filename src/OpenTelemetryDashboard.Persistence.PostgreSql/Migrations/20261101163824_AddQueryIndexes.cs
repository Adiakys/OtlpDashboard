using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_spans_resource_hash",
                table: "spans");

            migrationBuilder.DropIndex(
                name: "ix_log_records_resource_hash",
                table: "log_records");

            migrationBuilder.CreateIndex(
                name: "ix_spans_resource_hash_start_unix_nano",
                table: "spans",
                columns: new[] { "resource_hash", "start_unix_nano" });

            migrationBuilder.CreateIndex(
                name: "ix_spans_roots",
                table: "spans",
                columns: new[] { "start_unix_nano", "name", "status_code", "end_unix_nano" },
                filter: "parent_span_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_log_records_resource_hash_time_unix_nano",
                table: "log_records",
                columns: new[] { "resource_hash", "time_unix_nano" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_spans_resource_hash_start_unix_nano",
                table: "spans");

            migrationBuilder.DropIndex(
                name: "ix_spans_roots",
                table: "spans");

            migrationBuilder.DropIndex(
                name: "ix_log_records_resource_hash_time_unix_nano",
                table: "log_records");

            migrationBuilder.CreateIndex(
                name: "ix_spans_resource_hash",
                table: "spans",
                column: "resource_hash");

            migrationBuilder.CreateIndex(
                name: "ix_log_records_resource_hash",
                table: "log_records",
                column: "resource_hash");
        }
    }
}
