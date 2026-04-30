using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenTelemetryDashboard.Persistence.Sqlite.Migrations
{
    /// <summary>
    /// Aligns the EF model snapshot with the runtime model after the
    /// `HasData` seed for the default dashboard was removed (the
    /// `BuiltinDashboardSeeder` now owns that responsibility at runtime).
    /// Intentionally no-op at the SQL level: the historic
    /// `SeedDefaultDashboard` migration already inserted the row and
    /// users may have edited it — deleting it here would discard their
    /// work.
    /// </summary>
    public partial class RemoveDefaultDashboardSeed : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op. See class summary for rationale.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op.
        }
    }
}
