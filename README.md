# OpenTelemetry Dashboard

A self-hosted OTLP receiver and viewer for traces, logs, and metrics.
.NET 10 + EF Core backend, Vue 3 / Nuxt 4 SPA. Stores telemetry in SQLite,
PostgreSQL, or SQL Server. Speaks OTLP gRPC on `:4317` and OTLP
HTTP/Protobuf on `:4318` (the same port also serves the SPA).

---

## Quick start

### Docker (default: SQLite, persisted to a named volume)

```bash
docker compose up --build
# OTLP gRPC : localhost:4317
# OTLP HTTP : localhost:4318
# SPA       : http://localhost:4318
```

PostgreSQL or SQL Server instead of SQLite:

```bash
STORAGE_PROVIDER=PostgreSql docker compose --profile postgres  up --build
STORAGE_PROVIDER=SqlServer  docker compose --profile sqlserver up --build
```

### Local dev (no Docker)

```bash
# Backend
dotnet run --project src/OpenTelemetryDashboard.Host

# Frontend (separate terminal, hot-reload, proxies /api to :4318)
cd web && pnpm install && pnpm dev
```

The backend runs migrations on every boot, so the SQLite file appears at
`./src/OpenTelemetryDashboard.Host/data/telemetry.db` on first run.

---

## Configuration

All settings live under `Dashboard:*` and `OpenTelemetryDashboard:*` in
`appsettings.json` and can be overridden by environment variables using
double underscores (e.g. `Dashboard__BrowserToken`).

### Auth tokens

Both endpoints are **public by default**. Set either token to require auth:

| Variable                       | Protects                              |
|--------------------------------|---------------------------------------|
| `DASHBOARD__BROWSERTOKEN`      | The SPA + read API (`/api/v1/...`)    |
| `DASHBOARD__OTLP__APIKEY`      | OTLP ingestion (gRPC + HTTP)          |

The SPA prompts for the browser token on `/login` and stores it in memory
(30 min idle TTL). OTLP clients send the API key as
`Authorization: Bearer <token>` or as the `x-otlp-api-key` header.

### Retention (max days)

Background job that drops rows older than the configured age. `0` (default)
disables retention for that signal.

```jsonc
"Dashboard": {
  "TelemetryLimits": {
    "MaxLogDays": 14,
    "MaxTraceDays": 7,
    "MaxMetricDays": 30,
    "SweepIntervalMinutes": 60
  }
}
```

Or via env vars:

```bash
Dashboard__TelemetryLimits__MaxLogDays=14
Dashboard__TelemetryLimits__MaxTraceDays=7
Dashboard__TelemetryLimits__MaxMetricDays=30
Dashboard__TelemetryLimits__SweepIntervalMinutes=60
```

The sweep runs each `SweepIntervalMinutes` per signal independently — a
failure on one kind doesn't stop the others.

### Storage provider

```bash
Dashboard__Storage__Provider=Sqlite        # default
ConnectionStrings__Sqlite="Data Source=/app/data/telemetry.db"

Dashboard__Storage__Provider=PostgreSql
ConnectionStrings__PostgreSql="Host=...;Database=telemetry;Username=...;Password=..."

Dashboard__Storage__Provider=SqlServer
ConnectionStrings__SqlServer="Server=...;Database=telemetry;User Id=...;Password=...;TrustServerCertificate=True"
```

---

## Widgets

Dashboards are made of widgets dropped onto a 12-column grid. Three sources:

- **`std`** — built into the bundle (Stat, Line, Sparkline, Gauge, Bar gauge,
  Pie, Heatmap, Recent traces, Logs stream, Text).
- **`custom`** — preset of a builtin saved by the user, persisted in the DB.
- **`<library>`** — read-only widgets shipped by an installed library
  (filesystem or installed from a Git repo).

The picker dialog (`+ Add widget` while editing a dashboard) shows all
three groups in the same modal. The search box filters by name,
description, *or* source — type `std`, `custom`, or a library id to filter
a whole bucket.

### Creating a custom widget

1. Open a dashboard, click **Edit**, add a builtin widget.
2. Configure it (metric binding, thresholds, range, …).
3. In the config drawer click **Save as widget**, give it a name, icon, and
   default size.

It now appears in the picker under **My widgets**, with inline edit and
delete buttons that show on hover.

Notes:
- The seed config is captured *at save time* — editing the template later
  changes only metadata (name, description, icon, default size). The seed
  config is immutable; clone + delete to change it.
- Existing dashboard instances of a deleted custom widget render a
  placeholder, not a crash.

### Widget libraries

A widget library is a directory containing one or more widget definitions.
The dashboard scans `appsettings:Widgets:LibrariesPath` (default
`/var/lib/oteldash/libraries`) at startup and surfaces every valid library
in the picker as its own group.

Install one of two ways:

1. **Drop a folder.** Copy the library into the libraries path
   (volume-mount, Ansible, etc.). The next reload picks it up.
2. **Install from a Git repo** (planned, iter 4 of the roadmap). The
   server clones the repo into the libraries path; updates are explicit
   ("Update" button → `git fetch && git reset --hard <ref>`).

#### Repository / folder layout

```
my-widget-pack/
├── manifest.json
├── README.md           (optional — surfaced in the UI)
├── LICENSE             (optional)
└── widgets/
    ├── sla-tracker/
    │   └── widget.json
    ├── trace-heatmap/
    │   ├── widget.json
    │   └── icon.svg    (optional Phosphor override)
    └── error-budget/
        └── widget.json
```

`manifest.json`:

```json
{
  "id": "team-otel-pack",
  "name": "Team OTel Pack",
  "version": "1.2.0",
  "author": "platform@example.com",
  "license": "MIT",
  "description": "Curated widgets for service ownership reviews"
}
```

`id` must match the directory name.

#### Writing a widget

Each `widgets/<kindId>/widget.json` declares one widget. Two engines:

**`preset`** — wraps a builtin with a precooked config:

```json
{
  "name": "SLA Tracker",
  "description": "p99 latency with SLO thresholds",
  "icon": "i-ph-target",
  "defaultSize": { "w": 4, "h": 3 },
  "engine": "preset",
  "baseKind": "metric-stat",
  "defaultConfig": {
    "calc": "last",
    "unitKind": "ms",
    "decimals": 1,
    "thresholds": [
      { "value": 0,   "color": "#7AAA7A" },
      { "value": 200, "color": "#D9B566" },
      { "value": 500, "color": "#E27A3F" }
    ]
  }
}
```

`baseKind` is one of: `metric-stat`, `metric-line`, `metric-sparkline`,
`metric-gauge`, `metric-bar-gauge`, `metric-pie`, `metric-heatmap`,
`recent-traces`, `logs-stream`, `text`. The shape of `defaultConfig`
matches the corresponding form in the SPA — copy from a working instance
(`Save as widget` produces a valid one).

**`spec`** — Vega-Lite chart (planned, iter 2):

```json
{
  "name": "Trace heatmap",
  "icon": "i-ph-grid-four",
  "defaultSize": { "w": 6, "h": 4 },
  "engine": "spec",
  "spec": { "mark": "rect", "encoding": { "x": { ... } } }
}
```

Field rules enforced by the loader:

- `name` ≤ 64 chars.
- `description` ≤ 280 chars.
- `icon` matches `^i-(ph|lucide)-[a-z0-9-]+$`.
- `defaultSize.w` ∈ [1, 12], `defaultSize.h` ∈ [1, 24].
- `defaultConfig` ≤ 64 KiB; `spec` ≤ 256 KiB.

Invalid widgets are skipped (logged) and don't break the rest of the
library.

---

## Endpoints

| Method | Path                                  | Purpose                       |
|--------|---------------------------------------|-------------------------------|
| GET    | `/healthz`                            | Liveness probe (anonymous)    |
| POST   | `/v1/{traces,logs,metrics}`           | OTLP HTTP/Protobuf ingestion  |
| GET    | `/api/v1/traces`, `/logs`, `/metrics` | Query API (paginated)         |
| GET/POST/PUT/DELETE | `/api/v1/dashboards`     | Dashboards CRUD               |
| GET/POST/PUT/DELETE | `/api/v1/widgets/definitions` | Custom widgets CRUD     |

The OpenAPI spec is generated at `/openapi/v1.json` in `Development`.

---

## License

[MIT](LICENSE).
