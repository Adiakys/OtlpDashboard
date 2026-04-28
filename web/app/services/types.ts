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
}

export interface DashboardInfoDto {
  applicationName: string
  /** Null when the caller is unauthenticated (server hides the build version). */
  version: string | null
}

export interface InstrumentDto {
  resourceHash: string
  serviceName: string | null
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
  /** Optional time window. When both are absent, the full ring-buffer snapshot is returned. */
  from?: string
  to?: string
}

/**
 * Wire shape of the default dashboard. `layoutJson` is opaque to the server —
 * the SPA owns the per-widget config schema. `rowVersion` participates in
 * optimistic concurrency on save.
 */
export interface DashboardDto {
  id: string
  name: string
  layoutJson: string
  updatedAt: string
  rowVersion: number
}

export interface SaveDashboardRequest {
  name: string
  layoutJson: string
  rowVersion: number
}
