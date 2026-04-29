using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardCrud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "layout_json",
                table: "dashboards");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "dashboards",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateTable(
                name: "dashboard_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    x = table.Column<int>(type: "integer", nullable: false),
                    y = table.Column<int>(type: "integer", nullable: false),
                    w = table.Column<int>(type: "integer", nullable: false),
                    h = table.Column<int>(type: "integer", nullable: false),
                    config_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dashboard_widgets", x => x.id);
                    table.ForeignKey(
                        name: "fk_dashboard_widgets_dashboards_dashboard_id",
                        column: x => x.dashboard_id,
                        principalTable: "dashboards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dashboard_widgets_dashboard_id",
                table: "dashboard_widgets",
                column: "dashboard_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dashboard_widgets");

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "dashboards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "layout_json",
                table: "dashboards",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
