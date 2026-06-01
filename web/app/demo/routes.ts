import type {
  DashboardInfoDto,
  InstrumentDto,
  PackDto,
  PagedResponse,
  WidgetLibraryDto
} from '~/services/types'
import {
  SEVERITY_BUCKETS,
  severityBucketFromNumber,
  type SeverityBucket
} from '~/types/filters'
import { DEMO_LIBRARIES, DEMO_PACKS } from './data/libraries'
import { DEMO_SERVICES } from './data/services'
import {
  INSTRUMENT_CATALOG,
  findInstrumentByHash
} from './data/instruments'
import { generateMetricSeries } from './generators/metrics'
import {
  generateTraceDetail,
  generateTraceList
} from './generators/traces'
import { generateLogList } from './generators/logs'
import { DashboardStore, demoError } from './state/DashboardStore'
import { WidgetDefinitionStore } from './state/WidgetDefinitionStore'

export interface DemoRouterDeps {
  dashboards: DashboardStore
  widgets: WidgetDefinitionStore
}

export interface DemoRequest {
  method: string
  path: string
  query: Record<string, unknown>
  body: unknown
  /** True when the request carries a non-empty bearer token. Mirrors the
   *  real server's gating on `/v1/info` (version surfaces only for
   *  authenticated callers). */
  authenticated: boolean
}

/**
 * Single dispatch surface for the demo. Returns the handler's payload
 * (a typed DTO mirror of what the real backend would send) or throws a
 * demo-flavoured error. The fetcher converts the throw into an
 * ofetch-shaped rejection.
 */
export function dispatch(req: DemoRequest, deps: DemoRouterDeps): unknown {
  const { method, path, query, body } = req

  // ------------- /v1/info -----------------------------------------
  if (method === 'GET' && path === '/v1/info') {
    const dto: DashboardInfoDto = {
      applicationName: 'OTel Dashboard (Demo)',
      requireAuth: true,
      // Real server gates infra-shape fields behind auth — the demo
      // mirrors that so the sidebar's "v…" line only appears post-login.
      version: req.authenticated ? 'demo' : null,
      storageProvider: req.authenticated ? 'Demo (in-memory)' : null,
      // The demo retention values are illustrative — long enough that
      // the seeded 7-day dataset stays fully visible in the time-range
      // pickers.
      telemetryLimits: req.authenticated
        ? {
            maxLogDays: 30,
            maxTraceDays: 30,
            maxMetricDays: 30,
            sweepIntervalMinutes: 60
          }
        : null,
      queryLimits: req.authenticated
        ? { maxWindowHours: 24 * 30, maxLimit: 10_000 }
        : null
    }
    return dto
  }

  // ------------- /v1/metrics --------------------------------------
  if (method === 'GET' && path === '/v1/metrics') {
    const dtos: InstrumentDto[] = INSTRUMENT_CATALOG.map((i) => i.dto)
    return dtos
  }
  if (method === 'GET' && path === '/v1/metrics/services') {
    return [...DEMO_SERVICES].sort()
  }
  if (method === 'GET' && path === '/v1/metrics/points') {
    const resourceHash = stringParam(query, 'resourceHash')
    const scopeName = stringParam(query, 'scopeName')
    const instrumentName = stringParam(query, 'instrumentName')
    const kind = stringParam(query, 'kind')
    const includeAttributes = boolParam(query, 'includeAttributes')
    const spec = findInstrumentByHash(resourceHash, scopeName, instrumentName, kind)
    if (!spec) throw demoError(404, `Instrument not found in demo catalog`)

    const now = Date.now()
    const fromMs = parseTimeParam(query.from) ?? now - 15 * 60_000
    const toMs = parseTimeParam(query.to) ?? now
    return generateMetricSeries(spec, fromMs, toMs, includeAttributes)
  }

  // ------------- /v1/traces ---------------------------------------
  if (method === 'GET' && path === '/v1/traces/services') {
    return [...DEMO_SERVICES].sort()
  }
  if (method === 'GET' && path === '/v1/service-map') {
    const fromMs = parseTimeParam(query.from) ?? Date.now() - 60 * 60_000
    const toMs = parseTimeParam(query.to) ?? Date.now()
    const focus = optionalString(query, 'service')
    // Demo aggregation walks the same generated trace list and folds
    // it into nodes + edges in memory. Mirrors the SQL aggregation
    // semantically (node = service, edge = parent-child cross-service
    // pair, self-loops dropped). For the demo's volume (a few hundred
    // traces) the cost is negligible.
    const list = generateTraceList({
      fromMs, toMs,
      limit: 800,
      service: null,
      cursor: null
    })
    const nodeMap = new Map<string, { service: string; requestCount: number; errorCount: number }>()
    const edgeMap = new Map<string, { fromService: string; toService: string; callCount: number; errorCount: number }>()
    // Postgres and Redis are emitted in the demo as their own OTel
    // services for trace browsing convenience, but on the topology map
    // they're really backing dependencies of sample-server — they don't
    // expose a Server-kind ingress. Classify them as `dependency` so the
    // map renders them with the visual treatment the real backend uses
    // for synthesised peer.service nodes.
    const DEPENDENCY_SERVICES = new Set(['postgresql', 'redis'])
    for (const summary of list.items) {
      const t = summary as { traceId: string; rootStatusCode: string; serviceName: string | null }
      const detail = generateTraceDetail(t.traceId)
      for (const span of detail.spans) {
        const svc = span.serviceName ?? 'unknown'
        const node = nodeMap.get(svc) ?? { service: svc, requestCount: 0, errorCount: 0 }
        node.requestCount++
        if (span.statusCode === 'Error') node.errorCount++
        nodeMap.set(svc, node)
      }
      // Build edges by walking parent → child links across services.
      const byId = new Map<string, { service: string; statusCode: string }>()
      for (const span of detail.spans) {
        byId.set(span.spanId, { service: span.serviceName ?? 'unknown', statusCode: span.statusCode })
      }
      for (const span of detail.spans) {
        if (!span.parentSpanId) continue
        const parent = byId.get(span.parentSpanId)
        if (!parent) continue
        const childSvc = span.serviceName ?? 'unknown'
        if (parent.service === childSvc) continue
        const key = `${parent.service}|${childSvc}`
        const edge = edgeMap.get(key) ?? {
          fromService: parent.service,
          toService: childSvc,
          callCount: 0,
          errorCount: 0
        }
        edge.callCount++
        if (span.statusCode === 'Error') edge.errorCount++
        edgeMap.set(key, edge)
      }
    }
    let nodes = [...nodeMap.values()].map(n => {
      const isDep = DEPENDENCY_SERVICES.has(n.service)
      return {
        ...n,
        kind: isDep ? 'dependency' as const : 'service' as const,
        // For dependency nodes the drawer lets the user drill into
        // /traces filtered by the attribute that named the dep — for
        // postgres / redis, we surface peer.service as the canonical
        // OTel convention so the link matches what a real producer
        // would tag.
        attributeKey: isDep ? 'peer.service' : null
      }
    })
    let edges = [...edgeMap.values()]
    if (focus) {
      const keptEdges = edges.filter(e => e.fromService === focus || e.toService === focus)
      const keptServices = new Set<string>([focus])
      for (const e of keptEdges) { keptServices.add(e.fromService); keptServices.add(e.toService) }
      edges = keptEdges
      nodes = nodes.filter(n => keptServices.has(n.service))
    }
    return { nodes, edges }
  }

  if (method === 'GET' && path === '/v1/traces/aggregations') {
    const fromMs = parseTimeParam(query.from) ?? Date.now() - 60 * 60_000
    const toMs = parseTimeParam(query.to) ?? Date.now()
    const limit = numberParam(query, 'limit') ?? 10
    const service = optionalString(query, 'service')
    const metric = optionalString(query, 'metric') ?? 'count'
    // The full backend GROUP BYs at SQL; the demo walks the same
    // summary list it would surface to /v1/traces and aggregates in
    // memory. Coherent with the rest of the demo (same scenarios →
    // same counts), at the cost of refetching the list per call.
    const list = generateTraceList({
      fromMs, toMs,
      limit: 500, // fetch enough to aggregate meaningfully
      service,
      cursor: null
    })
    const buckets = new Map<string, { count: number, errorCount: number, sum: number, max: number }>()
    for (const t of list.items) {
      const summary = t as { rootSpanName: string, durationMs: number, rootStatusCode: string }
      const key = summary.rootSpanName
      const bucket = buckets.get(key) ?? { count: 0, errorCount: 0, sum: 0, max: 0 }
      bucket.count++
      if (summary.rootStatusCode === 'Error') bucket.errorCount++
      bucket.sum += summary.durationMs
      if (summary.durationMs > bucket.max) bucket.max = summary.durationMs
      buckets.set(key, bucket)
    }
    const items = [...buckets.entries()].map(([key, b]) => ({
      key,
      count: b.count,
      errorCount: b.errorCount,
      avgMs: b.sum / b.count,
      maxMs: b.max
    }))
    items.sort((a, b) => {
      switch (metric) {
        case 'errorRate': return (b.errorCount / b.count) - (a.errorCount / a.count)
        case 'avgMs': return b.avgMs - a.avgMs
        case 'maxMs': return b.maxMs - a.maxMs
        default: return b.count - a.count
      }
    })
    return { items: items.slice(0, limit) }
  }
  if (method === 'GET' && path === '/v1/traces') {
    const fromMs = parseTimeParam(query.from) ?? Date.now() - 60 * 60_000
    const toMs = parseTimeParam(query.to) ?? Date.now()
    const limit = numberParam(query, 'limit') ?? 25
    const services = collectServices(query)
    const serviceMatch = optionalString(query, 'serviceMatch') === 'any' ? 'any' : 'root'
    const noService = boolParam(query, 'noService')
    // Demo dataset has no unnamed services — every generator-emitted
    // span carries a real service.name. So the noService filter is
    // honoured by returning an empty list, semantically matching the
    // real backend's behaviour for a dataset without nameless rows.
    if (noService) {
      return { items: [], nextCursor: null }
    }
    // Cheap filters work on the summary; we want to fetch *more* than
    // `limit` so post-filtering still has a chance of hitting the
    // requested page size. The fudge factor is intentional — the demo
    // is small-scale, refetching costs nothing.
    const status = optionalString(query, 'status')
    const minMs = numberParam(query, 'minMs')
    const maxMs = numberParam(query, 'maxMs')
    const spanNameContains = optionalString(query, 'spanNameContains')
    const attrFilters = attrPairs(query)
    const hasPostFilters = !!status || minMs != null || maxMs != null
      || !!spanNameContains || attrFilters.length > 0
    const fetchLimit = hasPostFilters ? Math.max(limit * 4, 100) : limit
    const result = generateTraceList({
      fromMs,
      toMs,
      limit: fetchLimit,
      // The generator narrows scenario selection to a single seed
      // service for performance; the multi-value allow-list is then
      // enforced post-generation below.
      service: services && services.length === 1 ? services[0]! : null,
      cursor: optionalString(query, 'cursor')
    })
    if (services && services.length > 0) {
      const allow = new Set(services)
      // Default match anchors on the root (the summary's serviceName);
      // `serviceMatch=any` widens the test to every service the trace
      // touched (root + otherServiceNames), matching the real
      // backend's discovery semantics.
      result.items = result.items.filter(t => {
        const summary = t as { serviceName: string; otherServiceNames?: string[] }
        if (allow.has(summary.serviceName)) return true
        if (serviceMatch === 'any') {
          return summary.otherServiceNames?.some(s => allow.has(s)) ?? false
        }
        return false
      })
    }
    if (status === 'ok' || status === 'error') {
      const want = status === 'error' ? 'Error' : 'Ok'
      result.items = result.items.filter(t => (t as { rootStatusCode: string }).rootStatusCode === want)
    }
    if (minMs != null) {
      result.items = result.items.filter(t => (t as { durationMs: number }).durationMs >= minMs)
    }
    if (maxMs != null) {
      result.items = result.items.filter(t => (t as { durationMs: number }).durationMs <= maxMs)
    }
    // Span-name search and attribute filters both need to walk the
    // generated detail; combine them so we generate it at most once
    // per surviving summary.
    if (spanNameContains || attrFilters.length > 0) {
      const needle = spanNameContains?.toLowerCase()
      result.items = result.items.filter(t => {
        const detail = generateTraceDetail((t as { traceId: string }).traceId)
        if (needle && !detail.spans.some(s => s.name.toLowerCase().includes(needle))) return false
        for (const f of attrFilters) {
          if (!detail.spans.some(s => String((s.attributes as Record<string, unknown>)[f.key] ?? '') === f.value)) return false
        }
        return true
      })
    }
    if (result.items.length > limit) result.items = result.items.slice(0, limit)
    return result as PagedResponse<unknown>
  }
  {
    const m = /^\/v1\/traces\/([^/]+)$/.exec(path)
    if (m && method === 'GET') {
      return generateTraceDetail(decodeURIComponent(m[1]!))
    }
  }

  // ------------- /v1/logs -----------------------------------------
  if (method === 'GET' && path === '/v1/logs/services') {
    return [...DEMO_SERVICES].sort()
  }
  if (method === 'GET' && path === '/v1/logs') {
    const fromMs = parseTimeParam(query.from) ?? Date.now() - 15 * 60_000
    const toMs = parseTimeParam(query.to) ?? Date.now()
    const limit = numberParam(query, 'limit') ?? 50
    const services = collectServices(query)
    const severities = severityBuckets(query)
    const bodyContains = optionalString(query, 'bodyContains')
    const filters = attrPairs(query)
    const hasPostFilters = severities.size > 0 || !!bodyContains || filters.length > 0
      || (services !== undefined && services.length > 1)
    const fetchLimit = hasPostFilters ? Math.max(limit * 4, 200) : limit
    const result = generateLogList({
      fromMs,
      toMs,
      limit: fetchLimit,
      service: services && services.length === 1 ? services[0]! : null,
      minSeverity: numberParam(query, 'minSeverity'),
      traceIdFilter: optionalString(query, 'traceId')
    })
    if (services && services.length > 0) {
      const allow = new Set(services)
      result.items = result.items.filter(l => l.serviceName != null && allow.has(l.serviceName))
    }
    if (severities.size > 0) {
      result.items = result.items.filter(l => severities.has(severityBucketFromNumber(l.severityNumber)))
    }
    if (bodyContains) {
      const needle = bodyContains.toLowerCase()
      result.items = result.items.filter(l => (l.body ?? '').toLowerCase().includes(needle))
    }
    if (filters.length > 0) {
      result.items = result.items.filter(l =>
        filters.every(f => String((l.attributes as Record<string, unknown>)[f.key] ?? '') === f.value)
      )
    }
    if (result.items.length > limit) result.items = result.items.slice(0, limit)
    return result
  }

  // ------------- /v1/dashboards -----------------------------------
  if (path === '/v1/dashboards') {
    if (method === 'GET') return deps.dashboards.list()
    if (method === 'POST') return deps.dashboards.create(body as never)
  }
  {
    const m = /^\/v1\/dashboards\/(.+)$/.exec(path)
    if (m) {
      const id = decodeURIComponent(m[1]!)
      if (method === 'GET') return deps.dashboards.getById(id)
      if (method === 'PUT') return deps.dashboards.update(id, body as never)
      if (method === 'DELETE') {
        deps.dashboards.delete(id)
        return undefined
      }
    }
  }

  // ------------- /v1/widgets/definitions --------------------------
  if (path === '/v1/widgets/definitions') {
    if (method === 'GET') return deps.widgets.list()
    if (method === 'POST') return deps.widgets.create(body as never)
  }
  {
    const m = /^\/v1\/widgets\/definitions\/(.+)$/.exec(path)
    if (m) {
      const id = decodeURIComponent(m[1]!)
      if (method === 'GET') return deps.widgets.getById(id)
      if (method === 'PUT') return deps.widgets.update(id, body as never)
      if (method === 'DELETE') {
        deps.widgets.delete(id)
        return undefined
      }
    }
  }

  // ------------- /v1/widgets/libraries ----------------------------
  if (method === 'GET' && path === '/v1/widgets/libraries') {
    const out: WidgetLibraryDto[] = DEMO_LIBRARIES
    return out
  }

  // ------------- /v1/packs ----------------------------------------
  if (method === 'GET' && path === '/v1/packs') {
    const out: PackDto[] = DEMO_PACKS
    return out
  }
  if (method === 'POST' && path === '/v1/packs/reload') {
    return undefined
  }
  if (path.startsWith('/v1/packs')) {
    // install / update / uninstall — git-side actions don't make sense in
    // a static demo. Return a friendly 400 so the SPA's error toast shows
    // a meaningful reason if a user clicks one of these affordances.
    throw demoError(400, 'Pack install/update/uninstall is disabled in the demo build')
  }

  throw demoError(404, `Demo: no handler for ${method} ${path}`)
}

// ---------------- query helpers --------------------------------------

function stringParam(query: Record<string, unknown>, key: string): string {
  const v = query[key]
  if (typeof v === 'string' && v.length > 0) return v
  throw demoError(400, `Missing query parameter '${key}'`)
}

function optionalString(query: Record<string, unknown>, key: string): string | null {
  const v = query[key]
  return typeof v === 'string' && v.length > 0 ? v : null
}

function numberParam(query: Record<string, unknown>, key: string): number | undefined {
  const v = query[key]
  if (v === undefined || v === null || v === '') return undefined
  const n = Number(v)
  return Number.isFinite(n) ? n : undefined
}

function boolParam(query: Record<string, unknown>, key: string): boolean {
  const v = query[key]
  return v === true || v === 'true' || v === 1 || v === '1'
}

/**
 * Flatten the `services=` URL param (CSV or repeated) into a
 * deduplicated allow-list. Mirrors the C# `QueryValidation.CollectServiceNames`
 * so the demo and the real backend agree on incoming-request shape.
 * Returns `undefined` when nothing was supplied.
 */
function collectServices(query: Record<string, unknown>): string[] | undefined {
  const raw = query['services']
  const entries: string[] = Array.isArray(raw)
    ? (raw as unknown[]).filter((v): v is string => typeof v === 'string')
    : typeof raw === 'string' ? [raw] : []
  if (entries.length === 0) return undefined
  const out: string[] = []
  for (const entry of entries) {
    for (const part of entry.split(',')) {
      const t = part.trim()
      if (t.length > 0 && !out.includes(t)) out.push(t)
    }
  }
  return out.length > 0 ? out : undefined
}

/**
 * Parse `?attr=` repeated/CSV entries into key/value pairs. Mirrors
 * the server-side parser in C# QueryValidation. Used by the demo to
 * filter the generated trace/log lists in-memory — the demo data has
 * a single attribute (`demo.scenario`) on most rows, so the typical
 * filter request matches exactly the trace whose scenario we picked.
 */
function attrPairs(query: Record<string, unknown>): Array<{ key: string; value: string }> {
  const raw = query['attr']
  const entries: string[] = Array.isArray(raw)
    ? (raw as unknown[]).filter((v): v is string => typeof v === 'string')
    : typeof raw === 'string' ? [raw] : []
  const out: Array<{ key: string; value: string }> = []
  for (const entry of entries) {
    for (const part of entry.split(',')) {
      const trimmed = part.trim()
      if (!trimmed) continue
      const colon = trimmed.indexOf(':')
      if (colon < 0) continue
      const key = trimmed.slice(0, colon).trim()
      const value = trimmed.slice(colon + 1).trim()
      if (key && value) out.push({ key, value })
    }
  }
  return out
}

/**
 * Parse the `severities` query into a Set of recognized buckets. The
 * frontend sends a comma-joined list (e.g. `warn,error,fatal`); the
 * real backend also accepts repeated keys, so do both. Unknown tokens
 * are silently dropped — same behaviour as the C# QueryValidation.
 */
function severityBuckets(query: Record<string, unknown>): Set<SeverityBucket> {
  const raw = query['severities']
  const entries: string[] = Array.isArray(raw)
    ? (raw as unknown[]).filter((v): v is string => typeof v === 'string')
    : typeof raw === 'string' ? [raw] : []
  const out = new Set<SeverityBucket>()
  for (const entry of entries) {
    for (const part of entry.split(',')) {
      const t = part.trim().toLowerCase()
      if (t && (SEVERITY_BUCKETS as readonly string[]).includes(t)) {
        out.add(t as SeverityBucket)
      }
    }
  }
  return out
}

function parseTimeParam(value: unknown): number | null {
  if (typeof value === 'number') return value
  if (typeof value === 'string' && value.length > 0) {
    const t = Date.parse(value)
    if (Number.isFinite(t)) return t
    const n = Number(value)
    if (Number.isFinite(n)) return n
  }
  return null
}
