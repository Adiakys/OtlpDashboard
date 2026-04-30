# Demo built-in dashboards

`default.json` and `team-overview.json` are sample built-in dashboards
mounted by `docker-compose.yml` at `/app/data/dashboards`. The
`BuiltinDashboardSeeder` picks them up at boot:

| File | Resolved id | Notes |
|---|---|---|
| `default.json` | `00000000-0000-0000-0000-000000000001` (`Dashboard.DefaultId`) | Replaces the empty default created by the historic migration when the row is still pristine (no widgets, RowVersion = 0). Once a user saves a change to the default dashboard, the seeder leaves it alone. |
| `team-overview.json` | `b9...` (deterministic SHA-256 of the filename) | Shows the `id`-omitted convention: the same filename always yields the same Guid, so the dashboard is stable across deployments. |

Idempotency: the seeder runs every boot, but ids already present in the
store are skipped silently. To re-apply a built-in file, delete the
dashboard via the UI first (or drop the row directly).

For local development outside Docker, copy or symlink the files into
`src/OpenTelemetryDashboard.Host/data/dashboards/` (the path resolved by
the default `Dashboard:Dashboards:BuiltinPaths` configuration).
