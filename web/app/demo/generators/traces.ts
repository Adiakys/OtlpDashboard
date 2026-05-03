import type {
  PagedResponse,
  SpanDto,
  TraceDetailDto,
  TraceSummaryDto
} from '~/services/types'
import { DEMO_SERVICES } from '../data/services'
import { gaussian, hashString, mulberry32, pick, pickWeighted, range } from './prng'

interface OperationProfile {
  service: string
  name: string
  /** Lognormal mean (log-space). */
  durationLogMu: number
  /** Lognormal sigma (log-space). */
  durationLogSigma: number
  /** Probability of error (0-1). */
  errorRate: number
  /** Span count range [lo, hi]. */
  spanCount: [number, number]
}

/**
 * Plausible operations the demo stack would produce. Mix of HTTP routes,
 * DB queries, and Redis commands so the trace list looks like a real
 * polyglot service.
 */
const OPERATIONS: OperationProfile[] = [
  { service: 'sample-server', name: 'GET /api/v1/products',     durationLogMu: 4.0, durationLogSigma: 0.6, errorRate: 0.02, spanCount: [3, 8] },
  { service: 'sample-server', name: 'GET /api/v1/orders',       durationLogMu: 4.4, durationLogSigma: 0.7, errorRate: 0.04, spanCount: [4, 10] },
  { service: 'sample-server', name: 'POST /api/v1/orders',      durationLogMu: 4.9, durationLogSigma: 0.8, errorRate: 0.05, spanCount: [5, 12] },
  { service: 'sample-server', name: 'GET /api/v1/users/:id',    durationLogMu: 3.8, durationLogSigma: 0.5, errorRate: 0.01, spanCount: [3, 6] },
  { service: 'sample-server', name: 'GET /api/v1/health',       durationLogMu: 2.3, durationLogSigma: 0.3, errorRate: 0.001, spanCount: [1, 2] },
  { service: 'sample-server', name: 'POST /api/v1/checkout',    durationLogMu: 5.5, durationLogSigma: 0.9, errorRate: 0.07, spanCount: [6, 14] },
  { service: 'sample-client', name: 'background.refresh-cache', durationLogMu: 4.6, durationLogSigma: 0.7, errorRate: 0.03, spanCount: [2, 5] },
  { service: 'sample-client', name: 'background.send-email',    durationLogMu: 5.2, durationLogSigma: 0.8, errorRate: 0.06, spanCount: [3, 7] },
  { service: 'sample-client', name: 'cron.cleanup',             durationLogMu: 6.0, durationLogSigma: 1.0, errorRate: 0.02, spanCount: [4, 9] },
  { service: 'postgresql',    name: 'SELECT products',          durationLogMu: 3.0, durationLogSigma: 0.5, errorRate: 0.005, spanCount: [1, 1] },
  { service: 'postgresql',    name: 'SELECT orders',            durationLogMu: 3.6, durationLogSigma: 0.6, errorRate: 0.01, spanCount: [1, 1] },
  { service: 'postgresql',    name: 'INSERT order_items',       durationLogMu: 3.2, durationLogSigma: 0.5, errorRate: 0.01, spanCount: [1, 1] },
  { service: 'redis',         name: 'GET cache:product',        durationLogMu: 1.6, durationLogSigma: 0.3, errorRate: 0.001, spanCount: [1, 1] },
  { service: 'redis',         name: 'SET session',              durationLogMu: 1.5, durationLogSigma: 0.3, errorRate: 0.001, spanCount: [1, 1] },
  { service: 'redis',         name: 'EXPIRE session',           durationLogMu: 1.4, durationLogSigma: 0.3, errorRate: 0.001, spanCount: [1, 1] }
]

/**
 * Deterministic 16-byte trace id (hex). Two ids derived from the same
 * (seed, index) match across calls so log ↔ trace correlation works.
 */
function traceId(seed: number, index: number): string {
  const hi = (seed ^ index ^ 0x9e3779b9) >>> 0
  const lo = ((seed * 16777619) ^ (index * 65537)) >>> 0
  return (
    hi.toString(16).padStart(8, '0') +
    lo.toString(16).padStart(8, '0') +
    (index >>> 0).toString(16).padStart(8, '0') +
    (seed >>> 0).toString(16).padStart(8, '0')
  )
}

function spanId(seed: number, index: number): string {
  const v = (seed ^ (index * 0x9e3779b9)) >>> 0
  return v.toString(16).padStart(8, '0') + ((seed >>> 8) >>> 0).toString(16).padStart(8, '0')
}

/**
 * One trace summary — sampled lognormal duration, with timestamps that
 * fit inside the requested window.
 */
function makeSummary(
  rand: () => number,
  windowFromMs: number,
  windowToMs: number,
  index: number,
  seed: number,
  serviceFilter?: string | null
): TraceSummaryDto {
  const profile = serviceFilter
    ? pick(rand, OPERATIONS.filter((o) => o.service === serviceFilter)) ?? OPERATIONS[0]!
    : pick(rand, OPERATIONS)
  const durationMs = Math.max(
    1,
    Math.exp(profile.durationLogMu + gaussian(rand) * profile.durationLogSigma)
  )
  const endMs = windowToMs - rand() * (windowToMs - windowFromMs)
  const startMs = Math.max(windowFromMs, endMs - durationMs)
  const isError = rand() < profile.errorRate
  const spanCount =
    profile.spanCount[0] +
    Math.floor(rand() * (profile.spanCount[1] - profile.spanCount[0] + 1))

  return {
    traceId: traceId(seed, index),
    rootSpanName: profile.name,
    start: new Date(startMs).toISOString(),
    end: new Date(endMs).toISOString(),
    durationMs: Math.round(durationMs * 100) / 100,
    spanCount,
    rootStatusCode: isError ? 'Error' : 'Ok',
    resourceHash: `demo-${profile.service}`,
    serviceName: profile.service
  }
}

/**
 * Generate a paged list of trace summaries for the requested window.
 * Errors-first sort happens client-side in the widget — the demo just
 * returns chronological-descending data.
 */
export function generateTraceList(args: {
  fromMs: number
  toMs: number
  limit: number
  service?: string | null
  cursor?: string | null
}): PagedResponse<TraceSummaryDto> {
  // Density: roughly one trace per 6 seconds of window, capped by limit.
  const windowSec = (args.toMs - args.fromMs) / 1000
  const totalTraces = Math.min(args.limit, Math.max(8, Math.floor(windowSec / 6)))
  const seed = hashString(`traces|${args.fromMs}|${args.toMs}|${args.service ?? ''}`)
  const rand = mulberry32(seed)

  const items: TraceSummaryDto[] = []
  for (let i = 0; i < totalTraces; i++) {
    items.push(makeSummary(rand, args.fromMs, args.toMs, i, seed, args.service))
  }
  items.sort((a, b) => b.start.localeCompare(a.start))

  return { items, nextCursor: null }
}

/**
 * Generate a deterministic trace detail for any traceId — the SPA may
 * request detail for a trace it saw in a recent list, but it may also
 * deep-link to one. The id parses out enough entropy to reconstruct a
 * plausible structure without any database.
 */
export function generateTraceDetail(traceIdHex: string): TraceDetailDto {
  const seed = hashString(`detail|${traceIdHex}`)
  const rand = mulberry32(seed)
  const profile = pick(rand, OPERATIONS)
  const spanCount =
    profile.spanCount[0] +
    Math.floor(rand() * (profile.spanCount[1] - profile.spanCount[0] + 1))

  const totalMs = Math.exp(profile.durationLogMu + gaussian(rand) * profile.durationLogSigma)
  const startMs = Date.now() - totalMs - 1000
  const rootEndMs = startMs + totalMs

  const spans: SpanDto[] = []
  const rootSpanId = spanId(seed, 0)
  spans.push({
    spanId: rootSpanId,
    parentSpanId: null,
    name: profile.name,
    kind: profile.service === 'sample-server' ? 'Server' : 'Internal',
    start: new Date(startMs).toISOString(),
    end: new Date(rootEndMs).toISOString(),
    durationMs: Math.round(totalMs * 100) / 100,
    statusCode: rand() < profile.errorRate ? 'Error' : 'Ok',
    statusMessage: null,
    scopeName: 'demo.app',
    scopeVersion: '1.0.0',
    serviceName: profile.service,
    attributes: {
      'http.method': profile.name.split(' ')[0] ?? '',
      'http.route': profile.name.split(' ')[1] ?? ''
    },
    events: [],
    links: []
  })

  for (let i = 1; i < spanCount; i++) {
    const child = pick(rand, OPERATIONS.filter((o) => o.service !== profile.service))
    const childStart = startMs + range(rand, 0, totalMs * 0.7)
    const childDur = Math.min(
      totalMs - (childStart - startMs),
      Math.exp(child.durationLogMu + gaussian(rand) * child.durationLogSigma)
    )
    spans.push({
      spanId: spanId(seed, i),
      parentSpanId: rootSpanId,
      name: child.name,
      kind: child.service === 'postgresql' || child.service === 'redis' ? 'Client' : 'Internal',
      start: new Date(childStart).toISOString(),
      end: new Date(childStart + childDur).toISOString(),
      durationMs: Math.round(childDur * 100) / 100,
      statusCode: rand() < child.errorRate ? 'Error' : 'Ok',
      statusMessage: null,
      scopeName: 'demo.app',
      scopeVersion: '1.0.0',
      serviceName: child.service,
      attributes: {
        'db.system': child.service === 'postgresql' ? 'postgresql' : child.service === 'redis' ? 'redis' : ''
      },
      events: [],
      links: []
    })
  }

  return { traceId: traceIdHex, spans }
}

/** Used by the logs generator to attach a recent trace id to ~5% of logs. */
export function recentTraceIds(seed: number, count: number): string[] {
  const out: string[] = []
  for (let i = 0; i < count; i++) out.push(traceId(seed, i))
  return out
}

export { DEMO_SERVICES as TRACE_DEMO_SERVICES }
