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
The dashboard scans every entry in `Dashboard:Widgets:LibrariesPaths` (in
order) and surfaces valid libraries in the picker grouped by source. The
default is a single path `./data/widget-libraries`, which in Docker
resolves to `/app/data/widget-libraries` — already inside the
`dashboard-data` named volume, so drag-and-drop / git installs persist
across container restarts.

The shipped image already configures **two paths** in scan order:

1. `/app/data/widget-libraries` — runtime-managed (volume, git installs,
   drag-and-drop)
2. `/app/builtin-libraries` — baked into the image layer (no volume
   shadowing on rebuild)

Derived images don't need to set any environment variable — just `COPY`
into the second path:

```dockerfile
FROM opentelemetrydashboard:latest
COPY my-libs/ /app/builtin-libraries/
```

When two paths expose libraries with the same `manifest.id`, the first
in scan order wins and the rest are skipped with a warning — so a
runtime install can override a baked-in default by sharing its id.

A sample library lives at `widget-libraries-demo/demo-pack/` and is
bind-mounted by `docker-compose.yml` so `docker compose up --build`
shows a **demo-pack** section in the picker out of the box.

Install one of two ways:

1. **Drop a folder.** Copy the library into the libraries path
   (volume-mount, Ansible, etc.). Click the refresh icon in the widget
   picker (or `POST /api/v1/widgets/libraries/reload`) to re-scan.
2. **Install from a Git repo.** Click the git-branch icon in the picker
   header (or `POST /api/v1/widgets/libraries/install` with
   `{ url, ref }`). The server runs a shallow clone via LibGit2Sharp,
   parses `manifest.json`, resolves HEAD to a commit SHA, and atomically
   moves the directory into the runtime-managed root. Allowed hosts are
   `Dashboard:Widgets:AllowedGitHosts` (default `github.com, gitlab.com`).
   Use a tag for stable pinning; branches work but get a UI warning.
   Updates: "Update" button on git-installed library headers re-pulls
   the same ref (`fetch && reset --hard`).

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

**`spec`** — sandboxed HTML/SVG/CSS template with named metric bindings.
Use this for card-style UIs that the standard chart widgets can't
express: a database illustration whose fill level tracks a load
metric, a grid of service tiles whose colour follows a per-service
status metric, gauges with custom artwork, etc.

```json
{
  "name": "Database card",
  "icon": "i-ph-database",
  "defaultSize": { "w": 4, "h": 4 },
  "engine": "spec",
  "spec": {
    "template": "<div class='db'>\n  <svg>...</svg>\n  <div class='stats'>\n    <strong class='{{ thresholdClass load.value load.thresholds }}'>{{ format load.value 'percent' 0 }}</strong>\n    <span>QPS {{ format qps.value 'ops' 1 }}</span>\n  </div>\n</div>",
    "styles": ".db { display: flex; gap: 1rem; ... } .db .vellum-th-bad { color: var(--color-rust-600); }",
    "dataBindings": [
      { "name": "load", "type": "metric", "calc": "last", "unitKind": "percent",
        "thresholds": [{ "value": 0, "color": "#7AAA7A" }, { "value": 70, "color": "#D9B566" }, { "value": 90, "color": "#E27A3F" }] },
      { "name": "qps",  "type": "metric", "calc": "mean", "unitKind": "ops" }
    ]
  }
}
```

The user installing the library picks one instrument per binding
(`load`, `qps`, …) via the config drawer; the template/styles are
immutable and ship with the library.

**Template syntax** (Mustache-light, no JS evaluation):

| Construct | Meaning |
|---|---|
| `{{ name }}` / `{{ name.path }}` | Interpolate a value (HTML-escaped). |
| `{{ helper arg1 arg2 }}` | Call a whitelisted helper. |
| `{{#if expr}}…{{/if}}` | Render block when truthy. |
| `{{#each list as item}}…{{/each}}` | Loop, exposes `item` and `_index`. |

**Helpers available**: `format(value, unitKind, decimals?)`,
`percent(value, min, max)`, `thresholdColor(value, thresholds)`,
`thresholdClass(value, thresholds)` (returns `vellum-th-ok` / `-warn`
/ `-bad`), `dateAgo(timestamp)`, `pluralize(n, singular, plural)`,
`default(...values)`, comparators `eq`/`neq`/`lt`/`lte`/`gt`/`gte`
(usable inside `{{#if}}`).

**Binding shapes** the template scope sees:

- `metric` (default — `splitBy` not set): `{ value, unit, unitKind, thresholds }`.
- `metric` with `splitBy: "service.name"`: array of `{ key, value, attrs, thresholds }` — iterable with `{{#each}}`.
- `metric-series`: array of raw `{ time, value, attributes }` rows.

**Sandboxing**:

- The Mustache renderer never `eval`s — only whitelisted helpers run.
- DOMPurify (lazy-loaded ~50 KB gzip) sanitises the rendered HTML:
  no `<script>`, no `on*=`, no `javascript:` URLs, no `<iframe>`.
- CSS is auto-prefixed with the widget's instance scope so styles
  can't leak; `@import`, `@font-face`, `expression()`, and IE-era
  binding tricks are stripped before scoping.

Field rules enforced by the loader (`engine: spec`):

- `name` ≤ 64 chars.
- `description` ≤ 280 chars.
- `icon` matches `^i-(ph|lucide)-[a-z0-9-]+$`.
- `defaultSize.w` ∈ [1, 12], `defaultSize.h` ∈ [1, 24].
- `defaultConfig` ≤ 64 KiB; `spec` (the whole template+styles+bindings
  envelope) ≤ 256 KiB.

Invalid widgets are skipped (logged) and don't break the rest of the
library.

The demo pack at `widget-libraries-demo/demo-pack/` ships two
template-engine widgets — `db-card` (the database illustration above)
and `service-tile-grid` (one tile per service via `splitBy`) — that
double as live examples for authors.

---

## Built-in dashboards

Dashboards can be shipped to a deployment as JSON files, scanned at boot
by the `BuiltinDashboardSeeder`. The format matches the one the SPA
emits via "Export JSON" — drop an exported file in the scan path and
it's persisted on the next start.

The shipped image configures **two paths** in scan order:

1. `/app/data/dashboards` — runtime-managed (volume), drop files here
2. `/app/builtin-dashboards` — baked into the image layer (no volume
   shadowing on rebuild)

Derived images don't need any env-var override — just `COPY` JSONs into
the second path:

```dockerfile
FROM opentelemetrydashboard:latest
COPY my-dashboards/ /app/builtin-dashboards/
```

### File format

Same envelope produced by the SPA's export, plus an optional top-level
`id`:

```json
{
  "version": 1,
  "id": "00000000-0000-0000-0000-000000000001",
  "name": "Production Overview",
  "widgets": [
    { "id": "...", "kind": "std:metric-stat", "x": 0, "y": 0, "w": 4, "h": 3, "config": { /* opaque */ } }
  ]
}
```

### Id resolution (precedence)

1. **Explicit `id`** in the JSON wins, must parse as a Guid.
2. **`default.json`** filename → `00000000-0000-0000-0000-000000000001`
   (the well-known default dashboard id). Convenient for distributing a
   non-empty default.
3. **Any other filename** → SHA-256 of the filename, truncated to 16
   bytes (RFC 9562 v8). Stable across deployments — same filename
   always produces the same dashboard id.

### Idempotency

The seeder runs every boot but uses **skip-if-exists** semantics: an id
already in the store is left alone. The single special case is the
default dashboard — when `default.json` is present and the existing
default row is still pristine (no widgets, RowVersion 0), the seeder
upserts it once. Any user edit (saved widgets or RowVersion ≥ 1) makes
the seeder back off.

To re-apply a built-in file, delete the dashboard via the UI first.

A demo lives at `dashboards-demo/`, mounted by `docker-compose.yml`
into the runtime path.

---

## Endpoints

| Method | Path                                  | Purpose                       |
|--------|---------------------------------------|-------------------------------|
| GET    | `/healthz`                            | Liveness probe (anonymous)    |
| POST   | `/v1/{traces,logs,metrics}`           | OTLP HTTP/Protobuf ingestion  |
| GET    | `/api/v1/traces`, `/logs`, `/metrics` | Query API (paginated)         |
| GET/POST/PUT/DELETE | `/api/v1/dashboards`     | Dashboards CRUD               |
| GET/POST/PUT/DELETE | `/api/v1/widgets/definitions` | Custom widgets CRUD     |
| GET    | `/api/v1/widgets/libraries`           | Discovered widget libraries   |
| POST   | `/api/v1/widgets/libraries/reload`    | Re-scan the libraries path    |
| (boot) | seed dashboards from JSON             | Built-in dashboards loaded after migrations, idempotent |
| POST   | `/api/v1/widgets/libraries/install`   | Clone from git (`{url,ref}`)  |
| POST   | `/api/v1/widgets/libraries/{id}/update` | Re-pull a git-installed lib |
| DELETE | `/api/v1/widgets/libraries/{id}`      | Uninstall a library           |

The OpenAPI spec is generated at `/openapi/v1.json` in `Development`.

---

## License

[MIT](LICENSE).
