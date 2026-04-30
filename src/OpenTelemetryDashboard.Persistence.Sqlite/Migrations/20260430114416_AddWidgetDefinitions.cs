using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWidgetDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "widget_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 280, nullable: true),
                    icon = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    engine = table.Column<int>(type: "INTEGER", nullable: false),
                    base_kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    config_json = table.Column<string>(type: "TEXT", nullable: false),
                    spec_json = table.Column<string>(type: "TEXT", nullable: true),
                    default_w = table.Column<int>(type: "INTEGER", nullable: false),
                    default_h = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    row_version = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_widget_definitions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_widget_definitions_updated_at",
                table: "widget_definitions",
                column: "updated_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "widget_definitions");
        }
    }
}
