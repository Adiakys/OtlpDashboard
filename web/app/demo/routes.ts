import type {
  DashboardInfoDto,
  InstrumentDto,
  PagedResponse,
  WidgetLibraryDto
} from '~/services/types'
import { DEMO_LIBRARIES } from './data/libraries'
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
      queryMaxWindowHours: req.authenticated ? 24 * 30 : null
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
  if (method === 'GET' && path === '/v1/traces') {
    const fromMs = parseTimeParam(query.from) ?? Date.now() - 60 * 60_000
    const toMs = parseTimeParam(query.to) ?? Date.now()
    const limit = numberParam(query, 'limit') ?? 25
    const service = optionalString(query, 'service')
    const result: PagedResponse<unknown> = generateTraceList({
      fromMs,
      toMs,
      limit,
      service,
      cursor: optionalString(query, 'cursor')
    })
    return result
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
    return generateLogList({
      fromMs,
      toMs,
      limit,
      service: optionalString(query, 'service'),
      minSeverity: numberParam(query, 'minSeverity'),
      traceIdFilter: optionalString(query, 'traceId')
    })
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
  if (method === 'POST' && path === '/v1/widgets/libraries/reload') {
    return undefined
  }
  if (path.startsWith('/v1/widgets/libraries/')) {
    // install / update / uninstall — git-side actions don't make sense in
    // a static demo. Return a friendly 400 so the SPA's error toast shows
    // a meaningful reason if a user clicks one of these affordances.
    throw demoError(400, 'Library install/update/uninstall is disabled in the demo build')
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
