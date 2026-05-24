// OTLP/JSON export.
//
// Serialises in-memory logs / spans into the wire shape defined by
// `opentelemetry-proto` (ExportLogsServiceRequest, ExportTraceServiceRequest):
// the same envelope the OTel SDKs send and that downstream tools — the OTel
// collector's `fileexporter`/`fileexporter` consumer, Jaeger's "JSON File"
// loader, Tempo, Loki — round-trip without conversion.
//
// What the dashboard's DTOs *don't* carry round-trips imperfectly:
// per-span resource attributes (we only have `service.name`), and resource
// attributes on traces beyond the service name. We surface `resourceHash`
// under the conventional-ish `dashboard.resource_hash` key on log records so
// the export still groups identically when re-ingested by this tool.

import type { LogRecordDto, SpanDto, SpanEventDto, SpanLinkDto } from '~/services/types'

// --- OTLP wire shapes ----------------------------------------------------
//
// Typed against the proto3 JSON mapping. The int64 fields (`timeUnixNano`,
// `intValue`) come out as strings because JSON numbers lose precision past
// 2^53 — that's what every conformant OTLP/JSON reader expects.

interface OtlpAnyValue {
  stringValue?: string
  boolValue?: boolean
  intValue?: string
  doubleValue?: number
  arrayValue?: { values: OtlpAnyValue[] }
  kvlistValue?: { values: OtlpKeyValue[] }
}

interface OtlpKeyValue {
  key: string
  value: OtlpAnyValue
}

interface OtlpResource {
  attributes: OtlpKeyValue[]
}

interface OtlpInstrumentationScope {
  name: string
  version?: string
}

interface OtlpLogRecord {
  timeUnixNano: string
  observedTimeUnixNano?: string
  severityNumber?: number
  severityText?: string
  body?: OtlpAnyValue
  attributes: OtlpKeyValue[]
  traceId?: string
  spanId?: string
}

interface OtlpScopeLogs {
  scope: OtlpInstrumentationScope
  logRecords: OtlpLogRecord[]
}

interface OtlpResourceLogs {
  resource: OtlpResource
  scopeLogs: OtlpScopeLogs[]
}

interface OtlpSpanEvent {
  timeUnixNano: string
  name: string
  attributes: OtlpKeyValue[]
}

interface OtlpSpanLink {
  traceId: string
  spanId: string
  attributes: OtlpKeyValue[]
}

interface OtlpSpan {
  traceId: string
  spanId: string
  parentSpanId?: string
  name: string
  kind: string
  startTimeUnixNano: string
  endTimeUnixNano: string
  attributes: OtlpKeyValue[]
  events: OtlpSpanEvent[]
  links: OtlpSpanLink[]
  status: { code: string; message?: string }
}

interface OtlpScopeSpans {
  scope: OtlpInstrumentationScope
  spans: OtlpSpan[]
}

interface OtlpResourceSpans {
  resource: OtlpResource
  scopeSpans: OtlpScopeSpans[]
}

export interface OtlpLogsExport {
  resourceLogs: OtlpResourceLogs[]
}

export interface OtlpTracesExport {
  resourceSpans: OtlpResourceSpans[]
}

// --- Builders ------------------------------------------------------------

/**
 * Group a flat list of log records into OTLP `resourceLogs`. Bucketing is by
 * (resourceHash, serviceName) at the resource level and (scopeName,
 * scopeVersion) at the scope level — mirrors how the records were originally
 * grouped on the ingestion side, so re-ingesting the export reproduces the
 * same resource fingerprint.
 */
export function buildLogsExport(logs: LogRecordDto[]): OtlpLogsExport {
  // Two-level map keyed by stable, deterministic string keys. Using strings
  // (rather than nested Maps of objects) keeps the grouping order stable
  // across runs of the same input, which matters for snapshot tests.
  const resources = new Map<string, {
    resource: OtlpResource
    scopes: Map<string, OtlpScopeLogs>
  }>()

  for (const log of logs) {
    const resourceKey = `${log.resourceHash}|${log.serviceName ?? ''}`
    let bucket = resources.get(resourceKey)
    if (!bucket) {
      bucket = {
        resource: makeResource(log.serviceName, log.resourceHash),
        scopes: new Map()
      }
      resources.set(resourceKey, bucket)
    }
    const scopeKey = `${log.scopeName ?? ''}|${log.scopeVersion ?? ''}`
    let scope = bucket.scopes.get(scopeKey)
    if (!scope) {
      scope = {
        scope: { name: log.scopeName ?? '', version: log.scopeVersion ?? undefined },
        logRecords: []
      }
      bucket.scopes.set(scopeKey, scope)
    }
    scope.logRecords.push(toLogRecord(log))
  }

  return {
    resourceLogs: Array.from(resources.values(), b => ({
      resource: b.resource,
      scopeLogs: Array.from(b.scopes.values())
    }))
  }
}

/** Input to {@link buildSpansExport}: one trace's spans, tagged with the
 *  trace id they belong to so each emitted span carries the right
 *  `traceId` field (it lives on the span in OTLP, not above it). */
export interface TraceSpans {
  traceId: string
  spans: SpanDto[]
}

/**
 * Group spans from one or more traces into OTLP `resourceSpans`. The DTO
 * doesn't carry per-span resource attributes beyond `service.name`, so
 * resources collapse by service. Spans without `serviceName` land under an
 * empty-resource bucket — still valid OTLP, just unnamed.
 */
export function buildSpansExport(traces: TraceSpans[]): OtlpTracesExport {
  const resources = new Map<string, {
    resource: OtlpResource
    scopes: Map<string, OtlpScopeSpans>
  }>()

  for (const trace of traces) {
    for (const span of trace.spans) {
      const resourceKey = span.serviceName ?? ''
      let bucket = resources.get(resourceKey)
      if (!bucket) {
        bucket = {
          resource: makeResource(span.serviceName, null),
          scopes: new Map()
        }
        resources.set(resourceKey, bucket)
      }
      const scopeKey = `${span.scopeName ?? ''}|${span.scopeVersion ?? ''}`
      let scope = bucket.scopes.get(scopeKey)
      if (!scope) {
        scope = {
          scope: { name: span.scopeName ?? '', version: span.scopeVersion ?? undefined },
          spans: []
        }
        bucket.scopes.set(scopeKey, scope)
      }
      scope.spans.push(toSpan(trace.traceId, span))
    }
  }

  return {
    resourceSpans: Array.from(resources.values(), b => ({
      resource: b.resource,
      scopeSpans: Array.from(b.scopes.values())
    }))
  }
}

// --- Resource / attribute helpers ----------------------------------------

function makeResource(serviceName: string | null, resourceHash: string | null): OtlpResource {
  const attrs: OtlpKeyValue[] = []
  if (serviceName) attrs.push({ key: 'service.name', value: { stringValue: serviceName } })
  // `resourceHash` is dashboard-internal — the canonical fingerprint our
  // ingestion side derives from the source resource. Re-emitting it lets a
  // re-import collapse onto the same resource row without depending on the
  // full original attribute set.
  if (resourceHash) attrs.push({ key: 'dashboard.resource_hash', value: { stringValue: resourceHash } })
  return { attributes: attrs }
}

function toLogRecord(log: LogRecordDto): OtlpLogRecord {
  const record: OtlpLogRecord = {
    timeUnixNano: isoToUnixNano(log.time),
    attributes: toKeyValueList(log.attributes)
  }
  if (log.observedTime) record.observedTimeUnixNano = isoToUnixNano(log.observedTime)
  if (log.severityNumber > 0) record.severityNumber = log.severityNumber
  if (log.severityText) record.severityText = log.severityText
  if (log.body !== null && log.body !== undefined) record.body = { stringValue: log.body }
  if (log.traceId) record.traceId = log.traceId
  if (log.spanId) record.spanId = log.spanId
  return record
}

function toSpan(traceId: string, span: SpanDto): OtlpSpan {
  const out: OtlpSpan = {
    traceId,
    spanId: span.spanId,
    name: span.name,
    kind: mapSpanKind(span.kind),
    startTimeUnixNano: isoToUnixNano(span.start),
    endTimeUnixNano: isoToUnixNano(span.end),
    attributes: toKeyValueList(span.attributes),
    events: span.events.map(toSpanEvent),
    links: span.links.map(toSpanLink),
    status: mapStatus(span.statusCode, span.statusMessage)
  }
  if (span.parentSpanId) out.parentSpanId = span.parentSpanId
  return out
}

function toSpanEvent(e: SpanEventDto): OtlpSpanEvent {
  return {
    timeUnixNano: isoToUnixNano(e.time),
    name: e.name,
    attributes: toKeyValueList(e.attributes)
  }
}

function toSpanLink(l: SpanLinkDto): OtlpSpanLink {
  return {
    traceId: l.traceId,
    spanId: l.spanId,
    attributes: toKeyValueList(l.attributes)
  }
}

function toKeyValueList(attrs: Record<string, unknown> | null | undefined): OtlpKeyValue[] {
  if (!attrs) return []
  const out: OtlpKeyValue[] = []
  for (const [key, raw] of Object.entries(attrs)) {
    const value = toAnyValue(raw)
    if (value) out.push({ key, value })
  }
  return out
}

function toAnyValue(raw: unknown): OtlpAnyValue | null {
  if (raw === null || raw === undefined) return null
  if (typeof raw === 'string') return { stringValue: raw }
  if (typeof raw === 'boolean') return { boolValue: raw }
  if (typeof raw === 'number') {
    // Integers within the safe range round-trip as `intValue` (string in
    // proto3 JSON) so consumers don't see a float where the source was an
    // int. Anything else — fractional, ±Inf, NaN, beyond 2^53 — goes as
    // `doubleValue`. NaN/Inf can't be represented in strict JSON, so we
    // bail to a string so the file stays parseable.
    if (!Number.isFinite(raw)) return { stringValue: String(raw) }
    if (Number.isInteger(raw) && Number.isSafeInteger(raw)) return { intValue: String(raw) }
    return { doubleValue: raw }
  }
  if (typeof raw === 'bigint') return { intValue: raw.toString() }
  if (Array.isArray(raw)) {
    const values = raw.map(toAnyValue).filter((v): v is OtlpAnyValue => v !== null)
    return { arrayValue: { values } }
  }
  if (typeof raw === 'object') {
    return { kvlistValue: { values: toKeyValueList(raw as Record<string, unknown>) } }
  }
  return { stringValue: String(raw) }
}

// --- Enums ---------------------------------------------------------------

function mapSpanKind(kind: string): string {
  switch (kind) {
    case 'Internal': return 'SPAN_KIND_INTERNAL'
    case 'Server': return 'SPAN_KIND_SERVER'
    case 'Client': return 'SPAN_KIND_CLIENT'
    case 'Producer': return 'SPAN_KIND_PRODUCER'
    case 'Consumer': return 'SPAN_KIND_CONSUMER'
    default: return 'SPAN_KIND_UNSPECIFIED'
  }
}

function mapStatus(code: string, message: string | null): OtlpSpan['status'] {
  let mapped: string
  switch (code) {
    case 'Ok': mapped = 'STATUS_CODE_OK'; break
    case 'Error': mapped = 'STATUS_CODE_ERROR'; break
    default: mapped = 'STATUS_CODE_UNSET'
  }
  const status: OtlpSpan['status'] = { code: mapped }
  if (message) status.message = message
  return status
}

// --- Time conversion -----------------------------------------------------

/**
 * Convert an ISO-8601 timestamp to a nanoseconds-since-epoch string.
 * JavaScript `Date` rounds to milliseconds, so we extract the fractional
 * seconds separately and pad/truncate to 9 digits — keeps the sub-ms
 * precision the .NET side serialises (DateTimeOffset has 100ns ticks).
 */
export function isoToUnixNano(iso: string): string {
  const match = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(\.\d+)?(Z|[+-]\d{2}:?\d{2})?$/.exec(iso)
  if (!match) {
    const t = Date.parse(iso)
    return Number.isNaN(t) ? '0' : (BigInt(t) * 1_000_000n).toString()
  }
  const [, head, frac, tz] = match
  // `head` excludes the fractional component, so the parsed ms is
  // second-aligned — we add the full fractional-nanosecond contribution
  // below without any subtraction.
  const ms = Date.parse(`${head}${tz ?? 'Z'}`)
  if (Number.isNaN(ms)) return '0'
  let nanos = BigInt(ms) * 1_000_000n
  if (frac) {
    // Drop the leading '.', pad to 9 digits, truncate to 9. Preserves
    // .NET's 7-digit (100ns) precision exactly.
    const digits = (frac.slice(1) + '000000000').slice(0, 9)
    nanos += BigInt(digits)
  }
  return nanos.toString()
}

// --- File download -------------------------------------------------------

/**
 * Serialise `payload` and trigger a browser download. `.otlp.json` suffix
 * signals the format to anyone scanning the downloads folder.
 */
export function downloadOtlpJson(payload: OtlpLogsExport | OtlpTracesExport, baseName: string): void {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${baseName}-${todayStamp()}.otlp.json`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function todayStamp(): string {
  return new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)
}
