using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    resource_hash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                    scope_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    scope_version = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    name = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    kind = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    unit = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    is_monotonic = table.Column<bool>(type: "bit", nullable: false),
                    temporality = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.id);
                    table.ForeignKey(
                        name: "fk_instruments_resources_resource_hash",
                        column: x => x.resource_hash,
                        principalTable: "resources",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "metric_points",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    instrument_id = table.Column<long>(type: "bigint", nullable: false),
                    time_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    start_time_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    value = table.Column<double>(type: "float", nullable: false),
                    attributes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_metric_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_metric_points_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_resource_hash",
                table: "instruments",
                column: "resource_hash");

            migrationBuilder.CreateIndex(
                name: "ix_instruments_resource_hash_scope_name_name_kind",
                table: "instruments",
                columns: new[] { "resource_hash", "scope_name", "name", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_metric_points_instrument_id_time_unix_nano",
                table: "metric_points",
                columns: new[] { "instrument_id", "time_unix_nano" });

            migrationBuilder.CreateIndex(
                name: "ix_metric_points_time_unix_nano",
                table: "metric_points",
                column: "time_unix_nano");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "metric_points");

            migrationBuilder.DropTable(
                name: "instruments");
        }
    }
}
