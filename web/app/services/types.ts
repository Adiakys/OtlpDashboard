// DTO contracts mirror the C# records exposed by OpenTelemetryDashboard.Api.
// Keep in sync with `src/OpenTelemetryDashboard.Api/Contracts/*.cs`.

export interface PagedResponse<T> {
  items: T[]
  nextCursor: string | null
}

export interface LogRecordDto {
  time: string
  observedTime: string | null
  severityNumber: number
  severityText: string | null
  body: string | null
  traceId: string | null
  spanId: string | null
  scopeName: string | null
  scopeVersion: string | null
  resourceHash: string
  serviceName: string | null
  attributes: Record<string, unknown>
}

export interface TraceSummaryDto {
  traceId: string
  rootSpanName: string
  start: string
  end: string
  durationMs: number
  spanCount: number
  rootStatusCode: string
  resourceHash: string
  serviceName: string | null
  /** Distinct service.name values touched by spans of this trace
   *  OTHER than the root's. Empty when the trace stays inside one
   *  service. The list view renders the column as
   *  `{serviceName} (+N)` with this list as the tooltip. */
  otherServiceNames: string[]
}

export interface SpanEventDto {
  name: string
  time: string
  attributes: Record<string, unknown>
}

export interface SpanLinkDto {
  traceId: string
  spanId: string
  attributes: Record<string, unknown>
}

export interface SpanDto {
  spanId: string
  parentSpanId: string | null
  name: string
  kind: string
  start: string
  end: string
  durationMs: number
  statusCode: string
  statusMessage: string | null
  scopeName: string | null
  scopeVersion: string | null
  serviceName: string | null
  attributes: Record<string, unknown>
  events: SpanEventDto[]
  links: SpanLinkDto[]
}

export interface TraceDetailDto {
  traceId: string
  spans: SpanDto[]
  /** True when the trace contained more spans than the per-trace cap and
   *  the returned `spans` array is an early prefix. The SPA must surface
   *  this to the user — the trace is incomplete, not complete. */
  truncated: boolean
}

/** One row of the Top-N aggregation. The server fills all four
 *  metrics regardless of which one drove the sort, so the SPA can
 *  re-sort client-side without a refetch. */
export interface TraceAggregationItemDto {
  key: string
  count: number
  errorCount: number
  avgMs: number
  maxMs: number
}

export interface TraceAggregationsResponse {
  items: TraceAggregationItemDto[]
}

export type TraceAggregationMetric = 'count' | 'errorRate' | 'avgMs' | 'maxMs'

/** One node in the service-map: a service touched in the window
 *  with its total span count and error count. `kind` is `service`
 *  for OTel-emitting services and `dependency` for synthesised
 *  external entities (downstream services that don't emit telemetry
 *  of their own) inferred from kind=Client spans tagged with a
 *  peer-service attribute (`peer.service` or `service.peer.name`). */
export interface ServiceMapNodeDto {
  service: string
  kind: 'service' | 'dependency'
  requestCount: number
  errorCount: number
  /** Only populated for `kind === 'dependency'`: the attribute key
   *  whose value matches `service` (e.g. `peer.service`). The drawer
   *  uses it to build a precise `attr=key:value` drill-down link
   *  into /traces. */
  attributeKey?: string | null
}

/** A directed call edge from one service to another. Self-loops are
 *  filtered out at the source. */
export interface ServiceMapEdgeDto {
  fromService: string
  toService: string
  callCount: number
  errorCount: number
}

export interface ServiceMapDto {
  nodes: ServiceMapNodeDto[]
  edges: ServiceMapEdgeDto[]
}

export interface TimeWindow {
  from: string
  to: string
}

export interface PageQuery extends TimeWindow {
  limit?: number
  cursor?: string
  /** Optional log filter: restrict to records correlated with this trace. */
  traceId?: string
  /** Optional filter: restrict to rows whose resource `service.name`
   *  is in this allow-list. Empty/undefined disables the filter. The
   *  HTTP layer joins with commas as `services=A,B,C`. */
  services?: string[]
  /** Optional trace filter: restrict to traces touching at least one
   *  span whose Resource has no `service.name` (null or empty). Used
   *  by the service-map "(unnamed)" drill-down. Mutually exclusive
   *  with `service`. */
  noService?: boolean
  /** Service-match anchor for `services` and `noService`: `'root'`
   *  (default) matches the trace's root span only; `'any'` matches
   *  any span in the trace (cross-service discovery). */
  serviceMatch?: 'root' | 'any'
  /**
   * Optional log filter: drop records whose OTLP severity_number is below
   * this cutoff (inclusive). 0/undefined keeps everything. Indexed
   * server-side, so high cutoffs ('Warn'+/13, 'Error'+/17) avoid streaming
   * the noisy Info/Debug tail to the client.
   */
  minSeverity?: number
  /**
   * Optional log filter: keep only records whose severity falls into one of
   * the listed buckets. Server expands each bucket name to its OTLP
   * severity_number range. Empty/undefined disables the filter.
   */
  severities?: string[]
  /** Optional log filter: case-insensitive substring match on the body. */
  bodyContains?: string
  /** Optional trace filter: 'ok' | 'error'. Undefined means no filter. */
  status?: 'ok' | 'error'
  /** Optional trace filter: inclusive lower bound on duration in milliseconds. */
  minMs?: number
  /** Optional trace filter: inclusive upper bound on duration in milliseconds. */
  maxMs?: number
  /** Optional trace filter: substring match on any span name in the trace. */
  spanNameContains?: string
  /** Optional attribute filters in `key:value` form, AND'd together.
   *  Match semantics are string-typed only: the server matches the
   *  canonical `"key":"value"` JSON substring against the row's
   *  attribute column. Numeric/boolean attribute filtering is not
   *  supported in this version. */
  attr?: string[]
}

export interface TelemetryLimitsDto {
  /** Days of logs retained before auto-deletion. 0 = retained indefinitely. */
  maxLogDays: number
  /** Days of traces retained before auto-deletion. 0 = retained indefinitely. */
  maxTraceDays: number
  /** Days of metric points retained before auto-deletion. 0 = retained indefinitely. */
  maxMetricDays: number
  /** Frequency of the retention sweep, in minutes. */
  sweepIntervalMinutes: number
}

export interface DashboardInfoDto {
  applicationName: string
  /** Null when the caller is unauthenticated (server hides the build version). */
  version: string | null
  /** Storage provider name ("Sqlite" / "PostgreSql" / "SqlServer"). Null
   *  when unauthenticated — same gate as `version`. */
  storageProvider: string | null
  /** Null when unauthenticated — same gate as `version`, for the same reason. */
  telemetryLimits: TelemetryLimitsDto | null
  /** Server-side caps on a single Query-API call. Lets the SPA clamp
   *  pickers up-front (time-range, page size) rather than wait for a
   *  server-side rejection. Null when unauthenticated. */
  queryLimits: QueryLimitsDto | null
}

export interface QueryLimitsDto {
  /** Maximum time window in hours that the Query API accepts. */
  maxWindowHours: number
  /** Maximum page size the Query API accepts. */
  maxLimit: number
}

export interface InstrumentDto {
  resourceHash: string
  serviceName: string | null
  /** `service.instance.id` carried so two instruments with the same
   *  name+scope+service.name but coming from different resources (e.g.
   *  two databases under `service.name=postgresql`) display distinctly. */
  serviceInstanceId: string | null
  scopeName: string
  name: string
  /** Mirrors the server-side InstrumentKind enum (e.g. 'Gauge', 'Sum'). */
  kind: string
  description: string | null
  unit: string | null
  isMonotonic: boolean
  /** Mirrors the server-side AggregationTemporality enum. */
  temporality: string
  pointCount: number
}

export interface MetricPointDto {
  time: string
  startTime: string
  value: number
  attributes: Record<string, unknown>
}

export interface MetricSeriesDto {
  instrument: InstrumentDto
  points: MetricPointDto[]
  /** True when the server hit its per-series row cap before reaching the
   *  end of the requested window. The caller should narrow the window
   *  before refetching. */
  truncated: boolean
}

/** Identifies one instrument — the four fields form the server-side lookup key. */
export interface InstrumentRef {
  resourceHash: string
  scopeName: string
  instrumentName: string
  kind: string
}

export interface MetricPointsQuery extends InstrumentRef {
  /** Optional time window. When both are absent, the full series is returned. */
  from?: string
  to?: string
  /**
   * Hydrate the per-point attribute map. Off by default — single-value
   * widgets (Stat, Sparkline, Gauge) ignore attributes, so skipping the
   * JSON column saves both bytes and parse time on the wire. Widgets that
   * split-by an attribute key (Line, BarGauge, Pie, Heatmap) opt in.
   */
  includeAttributes?: boolean
}

/**
 * One placement of a widget on a dashboard's grid. `kind` picks the SPA
 * component (e.g. `metric-line`, `text`); `config` is opaque per-kind data
 * — the backend round-trips it without interpreting the shape.
 */
export interface DashboardWidgetDto {
  id: string
  kind: string
  x: number
  y: number
  w: number
  h: number
  config: Record<string, unknown>
}

/**
 * Wire shape of a dashboard. `rowVersion` participates in optimistic
 * concurrency on save: pass back the value most recently returned by the
 * server.
 */
export interface DashboardDto {
  id: string
  name: string
  widgets: DashboardWidgetDto[]
  updatedAt: string
  rowVersion: number
}

export interface SaveDashboardRequest {
  name: string
  widgets: DashboardWidgetDto[]
  rowVersion: number
}

/** Stable identifier of the seeded "default" dashboard. */
export const DEFAULT_DASHBOARD_ID = '00000000-0000-0000-0000-000000000001'

// =============================================================
// Widget definitions (custom widgets saved by the user)
// =============================================================

/**
 * Engine the renderer dispatches on. Mirrored from the server enum
 * (Preset = 0, Spec = 1, Composite = 2). Serialized as the enum *name* by
 * ASP.NET's default JSON options (`JsonStringEnumConverter`-like behavior
 * is the SPA's contract — see `JsonSerializerDefaults.Web`).
 */
export type WidgetEngine = 'Preset' | 'Spec' | 'Composite'

/**
 * Wire shape of a custom widget definition. `config` and `spec` are opaque
 * per-engine payloads — the backend stores them as text and round-trips
 * them as JSON elements.
 */
export interface WidgetDefinitionDto {
  id: string
  name: string
  description: string | null
  icon: string
  engine: WidgetEngine
  baseKind: string | null
  config: Record<string, unknown>
  spec: Record<string, unknown> | null
  defaultW: number
  defaultH: number
  updatedAt: string
  rowVersion: number
}

export interface SaveWidgetDefinitionRequest {
  name: string
  description: string | null
  icon: string
  engine: WidgetEngine
  baseKind: string | null
  config: Record<string, unknown>
  spec: Record<string, unknown> | null
  defaultW: number
  defaultH: number
  rowVersion: number
}

// =============================================================
// Widget libraries (filesystem-discovered packs)
// =============================================================

/** Origin of a pack directory. Drives the SPA's "Update" affordance —
 *  filesystem-installed packs are managed externally; git-installed
 *  ones can be re-pulled in place. */
export type PackInstallSource = 'Filesystem' | 'Git'

export interface LibraryWidgetDto {
  kindId: string
  name: string
  description: string | null
  icon: string
  engine: WidgetEngine
  baseKind: string | null
  config: Record<string, unknown> | null
  spec: Record<string, unknown> | null
  /** Optional typed parameter declarations rendered at the top of the
   *  config form. Substituted into `${param}` placeholders inside the
   *  metric binding at runtime. Shape: ParameterDecl[] (see
   *  `~/lib/htmlEngine/types.ts`). Server stays opaque on the schema. */
  parameters: unknown[] | null
  defaultW: number
  defaultH: number
}

/**
 * Wire shape for a library inside a pack. The picker calls
 * `GET /v1/widgets/libraries` for the flat list and groups by `packId`
 * when it needs to surface the parent pack's install/update affordance.
 */
export interface WidgetLibraryDto {
  id: string
  name: string
  description: string | null
  icon: string | null
  /** Id of the parent pack — the management UI joins on this to find
   *  the install/uninstall row to act on. */
  packId: string
  widgets: LibraryWidgetDto[]
}

/** Sub-entry of {@link PackDto} listing a dashboard the pack ships.
 *  `builtin: true` entries are seeded into the dashboards store on
 *  first boot; the rest are installable templates. */
export interface PackDashboardDto {
  id: string
  builtin: boolean
}

/** One service-map icon shipped by a pack. The SPA's icon resolver
 *  walks the pack catalog (in pack-load order) and evaluates `match[]`
 *  in declaration order; the first hit determines the icon used for a
 *  given service-map node. */
export interface PackIconDto {
  id: string
  name: string
  /** Server-resolved URL the SPA renders directly inside an
   *  `<image>` tag — points at the pack asset endpoint in real
   *  builds, at a bundled `web/public/` path in the demo. */
  imageUrl: string
  match: PackIconMatchDto[]
}

/** Single matcher rule under {@link PackIconDto.match}. Exactly one
 *  field is set; the resolver throws if it sees both, but the wire
 *  shape just declares them as optional. */
export interface PackIconMatchDto {
  serviceName?: string | null
  namePattern?: string | null
}

/** Wire shape for an installed pack. Returned by `GET /v1/packs` and
 *  by the install endpoint. */
export interface PackDto {
  id: string
  name: string
  version: string
  author: string | null
  license: string | null
  description: string | null
  homepage: string | null
  installSource: PackInstallSource
  gitUrl: string | null
  gitRef: string | null
  gitRefResolved: string | null
  gitSubPath: string | null
  installedAt: string | null
  /** True only when the pack lives in the runtime-managed root (the
   *  first configured path). Drives the visibility of the uninstall
   *  button — baked-in packs from image layers are read-only. */
  removable: boolean
  libraries: WidgetLibraryDto[]
  dashboards: PackDashboardDto[]
  icons: PackIconDto[]
}
