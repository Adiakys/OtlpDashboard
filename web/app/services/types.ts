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
  /** Optional filter: restrict to rows whose resource `service.name` matches. */
  service?: string
  /**
   * Optional log filter: drop records whose OTLP severity_number is below
   * this cutoff (inclusive). 0/undefined keeps everything. Indexed
   * server-side, so high cutoffs ('Warn'+/13, 'Error'+/17) avoid streaming
   * the noisy Info/Debug tail to the client.
   */
  minSeverity?: number
}

export interface DashboardInfoDto {
  applicationName: string
  /** Null when the caller is unauthenticated (server hides the build version). */
  version: string | null
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

/** Origin of a library directory. Drives the SPA's "Update" affordance —
 *  filesystem-installed libraries are managed externally; git-installed
 *  ones can be re-pulled in place (iter 4). */
export type LibraryInstallSource = 'Filesystem' | 'Git'

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

export interface WidgetLibraryDto {
  id: string
  name: string
  version: string
  author: string | null
  license: string | null
  description: string | null
  installSource: LibraryInstallSource
  /** Git origin URL — null for filesystem-installed libraries. */
  gitUrl: string | null
  gitRef: string | null
  gitRefResolved: string | null
  installedAt: string | null
  /** True only when the library lives in the runtime-managed root (the
   *  first configured path). Drives the visibility of the uninstall
   *  button — baked-in libraries from image layers are read-only. */
  removable: boolean
  widgets: LibraryWidgetDto[]
}
