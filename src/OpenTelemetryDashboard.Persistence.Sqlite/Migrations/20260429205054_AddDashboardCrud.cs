using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.Sqlite.Migrations
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

            migrationBuilder.CreateTable(
                name: "dashboard_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    x = table.Column<int>(type: "INTEGER", nullable: false),
                    y = table.Column<int>(type: "INTEGER", nullable: false),
                    w = table.Column<int>(type: "INTEGER", nullable: false),
                    h = table.Column<int>(type: "INTEGER", nullable: false),
                    config_json = table.Column<string>(type: "TEXT", nullable: false)
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

            migrationBuilder.AddColumn<string>(
                name: "layout_json",
                table: "dashboards",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
