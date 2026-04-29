using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.SqlServer.Migrations
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
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.CreateTable(
                name: "dashboard_widgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    dashboard_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    x = table.Column<int>(type: "int", nullable: false),
                    y = table.Column<int>(type: "int", nullable: false),
                    w = table.Column<int>(type: "int", nullable: false),
                    h = table.Column<int>(type: "int", nullable: false),
                    config_json = table.Column<string>(type: "nvarchar(max)", nullable: false)
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
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "layout_json",
                table: "dashboards",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
