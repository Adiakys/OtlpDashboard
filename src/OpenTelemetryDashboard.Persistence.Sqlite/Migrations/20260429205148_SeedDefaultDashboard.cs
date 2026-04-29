using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "dashboards",
                columns: new[] { "id", "name", "row_version", "updated_at" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), "main", 0u, new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "dashboards",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000001"));
        }
    }
}
