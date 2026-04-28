using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.PostgreSql.Migrations
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
                    hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    service_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    service_instance_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    schema_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    dropped_attributes_count = table.Column<long>(type: "bigint", nullable: false),
                    attributes = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resources", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "log_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    resource_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    time_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    observed_time_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    severity_number = table.Column<int>(type: "integer", nullable: false),
                    severity_text = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    body = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    trace_id = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false),
                    span_id = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false),
                    flags = table.Column<long>(type: "bigint", nullable: false),
                    scope_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    scope_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attributes = table.Column<string>(type: "text", nullable: false),
                    dropped_attributes_count = table.Column<long>(type: "bigint", nullable: false)
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
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    trace_id = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false),
                    span_id = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false),
                    resource_hash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    parent_span_id = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    start_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    end_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    status_code = table.Column<int>(type: "integer", nullable: false),
                    status_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    scope_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    scope_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    flags = table.Column<long>(type: "bigint", nullable: false),
                    attributes = table.Column<string>(type: "text", nullable: false),
                    dropped_attributes_count = table.Column<long>(type: "bigint", nullable: false),
                    dropped_events_count = table.Column<long>(type: "bigint", nullable: false),
                    dropped_links_count = table.Column<long>(type: "bigint", nullable: false)
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
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    time_unix_nano = table.Column<long>(type: "bigint", nullable: false),
                    attributes = table.Column<string>(type: "text", nullable: false),
                    dropped_attributes_count = table.Column<long>(type: "bigint", nullable: false),
                    owner_span_id = table.Column<long>(type: "bigint", nullable: false)
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
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    linked_trace_id = table.Column<byte[]>(type: "bytea", maxLength: 16, nullable: false),
                    linked_span_id = table.Column<byte[]>(type: "bytea", maxLength: 8, nullable: false),
                    flags = table.Column<long>(type: "bigint", nullable: false),
                    attributes = table.Column<string>(type: "text", nullable: false),
                    dropped_attributes_count = table.Column<long>(type: "bigint", nullable: false),
                    owner_span_id = table.Column<long>(type: "bigint", nullable: false)
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
