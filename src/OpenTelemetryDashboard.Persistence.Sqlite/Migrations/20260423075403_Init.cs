using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resources",
                columns: table => new
                {
                    hash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    service_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    service_instance_id = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    schema_url = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    dropped_attributes_count = table.Column<uint>(type: "INTEGER", nullable: false),
                    attributes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resources", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "log_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    resource_hash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    time_unix_nano = table.Column<long>(type: "INTEGER", nullable: false),
                    observed_time_unix_nano = table.Column<long>(type: "INTEGER", nullable: false),
                    severity_number = table.Column<int>(type: "INTEGER", nullable: false),
                    severity_text = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    body = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    trace_id = table.Column<byte[]>(type: "BLOB", maxLength: 16, nullable: false),
                    span_id = table.Column<byte[]>(type: "BLOB", maxLength: 8, nullable: false),
                    flags = table.Column<uint>(type: "INTEGER", nullable: false),
                    scope_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    scope_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    attributes = table.Column<string>(type: "TEXT", nullable: false),
                    dropped_attributes_count = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_log_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_log_records_resources_resource_hash",
                        column: x => x.resource_hash,
                        principalTable: "resources",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "spans",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    trace_id = table.Column<byte[]>(type: "BLOB", maxLength: 16, nullable: false),
                    span_id = table.Column<byte[]>(type: "BLOB", maxLength: 8, nullable: false),
                    resource_hash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    parent_span_id = table.Column<byte[]>(type: "BLOB", maxLength: 8, nullable: true),
                    kind = table.Column<int>(type: "INTEGER", nullable: false),
                    start_unix_nano = table.Column<long>(type: "INTEGER", nullable: false),
                    end_unix_nano = table.Column<long>(type: "INTEGER", nullable: false),
                    status_code = table.Column<int>(type: "INTEGER", nullable: false),
                    status_message = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    scope_name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    scope_version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    flags = table.Column<uint>(type: "INTEGER", nullable: false),
                    attributes = table.Column<string>(type: "TEXT", nullable: false),
                    dropped_attributes_count = table.Column<uint>(type: "INTEGER", nullable: false),
                    dropped_events_count = table.Column<uint>(type: "INTEGER", nullable: false),
                    dropped_links_count = table.Column<uint>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spans", x => x.id);
                    table.ForeignKey(
                        name: "fk_spans_resources_resource_hash",
                        column: x => x.resource_hash,
                        principalTable: "resources",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "span_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    time_unix_nano = table.Column<long>(type: "INTEGER", nullable: false),
                    attributes = table.Column<string>(type: "TEXT", nullable: false),
                    dropped_attributes_count = table.Column<uint>(type: "INTEGER", nullable: false),
                    owner_span_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_span_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_span_events_spans_owner_span_id",
                        column: x => x.owner_span_id,
                        principalTable: "spans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "span_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    linked_trace_id = table.Column<byte[]>(type: "BLOB", maxLength: 16, nullable: false),
                    linked_span_id = table.Column<byte[]>(type: "BLOB", maxLength: 8, nullable: false),
                    flags = table.Column<uint>(type: "INTEGER", nullable: false),
                    attributes = table.Column<string>(type: "TEXT", nullable: false),
                    dropped_attributes_count = table.Column<uint>(type: "INTEGER", nullable: false),
                    owner_span_id = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_span_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_span_links_spans_owner_span_id",
                        column: x => x.owner_span_id,
                        principalTable: "spans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_log_records_resource_hash",
                table: "log_records",
                column: "resource_hash");

            migrationBuilder.CreateIndex(
                name: "ix_log_records_severity_number",
                table: "log_records",
                column: "severity_number");

            migrationBuilder.CreateIndex(
                name: "ix_log_records_time_unix_nano",
                table: "log_records",
                column: "time_unix_nano");

            migrationBuilder.CreateIndex(
                name: "ix_log_records_trace_id",
                table: "log_records",
                column: "trace_id");

            migrationBuilder.CreateIndex(
                name: "ix_resources_service_name",
                table: "resources",
                column: "service_name");

            migrationBuilder.CreateIndex(
                name: "ix_span_events_owner_span_id",
                table: "span_events",
                column: "owner_span_id");

            migrationBuilder.CreateIndex(
                name: "ix_span_links_owner_span_id",
                table: "span_links",
                column: "owner_span_id");

            migrationBuilder.CreateIndex(
                name: "ix_spans_resource_hash",
                table: "spans",
                column: "resource_hash");

            migrationBuilder.CreateIndex(
                name: "ix_spans_start_unix_nano",
                table: "spans",
                column: "start_unix_nano");

            migrationBuilder.CreateIndex(
                name: "ix_spans_trace_id",
                table: "spans",
                column: "trace_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "log_records");

            migrationBuilder.DropTable(
                name: "span_events");

            migrationBuilder.DropTable(
                name: "span_links");

            migrationBuilder.DropTable(
                name: "spans");

            migrationBuilder.DropTable(
                name: "resources");
        }
    }
}
