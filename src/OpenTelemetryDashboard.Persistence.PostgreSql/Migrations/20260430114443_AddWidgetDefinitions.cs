using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.PostgreSql.Migrations
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
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "character varying(280)", maxLength: 280, nullable: true),
                    icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    engine = table.Column<int>(type: "integer", nullable: false),
                    base_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    config_json = table.Column<string>(type: "text", nullable: false),
                    spec_json = table.Column<string>(type: "text", nullable: true),
                    default_w = table.Column<int>(type: "integer", nullable: false),
                    default_h = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
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
