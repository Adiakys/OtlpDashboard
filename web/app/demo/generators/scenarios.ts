import type { LogRecordDto, SpanDto } from '~/services/types'
import { gaussian, hashString, mulberry32 } from './prng'

/**
 * Demo scenarios — TS port of the C# `HistoricalDataSeeder` story
 * catalogue. Same six scripts (cache hit/miss, write deadlock,
 * validation error, deep batch, healthcheck) so what an installed
 * dashboard shows on first boot matches what the GitHub-Pages demo
 * shows. Each scenario fully determines:
 *
 *  - the root span (name, scope, kind, lognormal timing)
 *  - the child spans (offsets/durations as fractions of the root,
 *    optional `parentIndex` for deep trees)
 *  - the log script for the success and error variants — each line
 *    targets a specific span and a fraction of its duration, so
 *    log timestamps land naturally inside their owning span
 *
 * Trace IDs encode the trace's wall-clock start time in their first
 * 16 hex chars (lowercase Unix-ms padded), which lets
 * <see cref="generateTraceDetail"/> and <see cref="buildScenarioLogs"/>
 * reconstruct the same timestamps the trace summary displayed —
 * without that round-trip the detail page picked "now" and the
 * log times were unrelated to the list time.
 */

type SeverityName = 'Debug' | 'Info' | 'Warn' | 'Error'

const SEVERITY_NUMBER: Record<SeverityName, number> = {
  Debug: 5, Info: 9, Warn: 13, Error: 17
}

export interface SpanLayout {
  name: string
  scope: string
  kind: 'Server' | 'Client' | 'Internal'
  startFraction: number
  durationFraction: number
  /** 0 = root, 1+ = the (i-1)th previous child. Default: 0. */
  parentIndex?: number
}

export interface LogScript {
  body: string
  scope: string
  severity: SeverityName
  /** 0 = root, 1+ = children[i-1]. */
  attachSpanIndex: number
  /** [0,1] within the target span's duration. */
  timeFraction: number
}

export interface Scenario {
  id: string
  rootName: string
  rootScope: string
  rootKind: 'Server' | 'Internal'
  mu: number
  sigma: number
  weight: number
  errorRate: number
  children: SpanLayout[]
  successLogs: LogScript[]
  errorLogs: LogScript[]
  /** Index into children of the span that "caused" the error. The root
   *  is always also marked Error (matches OTel convention). */
  errorSpanIndex: number
  /** All services touched by spans of this scenario — derived from the
   *  scope mapping. Used by the service filter so a scenario stays
   *  selectable as long as it touches the requested service. */
  services: string[]
  errorMessage: string
}

/** Map a scope name to a "service.name". Mirrors what a real .NET
 *  app's OTel exporters do: the host service stays sample-server but
 *  client-style spans (Npgsql, StackExchange.Redis) carry their own
 *  service for the filter dropdown's polyglot view. */
function serviceFromScope(scope: string): string {
  if (scope.startsWith('Npgsql')) return 'postgresql'
  if (scope.startsWith('StackExchange.Redis')) return 'redis'
  return 'sample-server'
}

function withServices(s: Omit<Scenario, 'services'>): Scenario {
  // sample-client is always present because planTrace wraps every
  // scenario in a synthetic outer Client span emitted by sample-client —
  // the upstream caller of every sample-server request.
  const set = new Set<string>(['sample-client', serviceFromScope(s.rootScope)])
  for (const c of s.children) set.add(serviceFromScope(c.scope))
  return { ...s, services: [...set] }
}

export const SCENARIOS: Scenario[] = [
  withServices({
    id: 'get_counter_cache_hit',
    rootName: 'GET /counter',
    rootScope: 'Microsoft.AspNetCore',
    rootKind: 'Server',
    mu: 3.0, sigma: 0.45, weight: 38, errorRate: 0.005,
    errorMessage: 'Redis lock acquisition failed',
    children: [
      { name: 'redis.get counter:1',     scope: 'StackExchange.Redis', kind: 'Client',   startFraction: 0.05, durationFraction: 0.20 },
      { name: 'counter.serialize',       scope: 'SampleServer.Counter', kind: 'Internal', startFraction: 0.55, durationFraction: 0.30 }
    ],
    successLogs: [
      { body: "HybridCache hit for key 'counter:1'",                         scope: 'SampleServer.Cache',           severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.5 },
      { body: 'Request finished HTTP/1.1 GET /counter 200 in {ms}ms',         scope: 'Microsoft.AspNetCore.Hosting', severity: 'Info',  attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorLogs: [
      { body: 'Redis backpressure: pending={n}, retrying with exponential backoff', scope: 'SampleServer.Cache',           severity: 'Warn',  attachSpanIndex: 1, timeFraction: 0.4 },
      { body: 'Failed to acquire Redis lock for counter:1 — request aborted',       scope: 'SampleServer.Cache',           severity: 'Error', attachSpanIndex: 1, timeFraction: 0.7 },
      { body: 'Request finished HTTP/1.1 GET /counter 503 in {ms}ms',                scope: 'Microsoft.AspNetCore.Hosting', severity: 'Error', attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorSpanIndex: 1
  }),
  withServices({
    id: 'get_counter_cache_miss',
    rootName: 'GET /counter',
    rootScope: 'Microsoft.AspNetCore',
    rootKind: 'Server',
    mu: 3.9, sigma: 0.55, weight: 12, errorRate: 0.02,
    errorMessage: 'Database read failed after cache miss',
    children: [
      { name: 'redis.get counter:1',     scope: 'StackExchange.Redis', kind: 'Client', startFraction: 0.05, durationFraction: 0.10 },
      { name: 'pg.query SELECT counter', scope: 'Npgsql',              kind: 'Client', startFraction: 0.20, durationFraction: 0.55 },
      { name: 'redis.set counter:1',     scope: 'StackExchange.Redis', kind: 'Client', startFraction: 0.80, durationFraction: 0.10 }
    ],
    successLogs: [
      { body: "HybridCache miss for key 'counter:1', falling back to database", scope: 'SampleServer.Cache',           severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.5 },
      { body: 'Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]',                scope: 'Microsoft.EntityFrameworkCore', severity: 'Debug', attachSpanIndex: 2, timeFraction: 0.6 },
      { body: 'Request finished HTTP/1.1 GET /counter 200 in {ms}ms',            scope: 'Microsoft.AspNetCore.Hosting',  severity: 'Info',  attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorLogs: [
      { body: "HybridCache miss for key 'counter:1', falling back to database", scope: 'SampleServer.Cache',           severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.4 },
      { body: 'Slow query detected ({ms}ms > 250ms threshold) — counter table',  scope: 'SampleServer.Performance',     severity: 'Warn',  attachSpanIndex: 2, timeFraction: 0.6 },
      { body: 'Database read failed: connection reset by peer',                  scope: 'Microsoft.EntityFrameworkCore', severity: 'Error', attachSpanIndex: 2, timeFraction: 0.7 },
      { body: 'Request finished HTTP/1.1 GET /counter 500 in {ms}ms',            scope: 'Microsoft.AspNetCore.Hosting',  severity: 'Error', attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorSpanIndex: 2
  }),
  withServices({
    id: 'post_counter_value',
    rootName: 'POST /counter/{value}',
    rootScope: 'Microsoft.AspNetCore',
    rootKind: 'Server',
    mu: 4.0, sigma: 0.50, weight: 14, errorRate: 0.04,
    errorMessage: 'Database deadlock during counter mutation',
    children: [
      { name: 'counter.mutate',          scope: 'SampleServer.Counter', kind: 'Internal', startFraction: 0.05, durationFraction: 0.30 },
      { name: 'pg.query UPDATE counter', scope: 'Npgsql',               kind: 'Client',   startFraction: 0.40, durationFraction: 0.50 }
    ],
    successLogs: [
      { body: 'Counter mutation accepted (delta={n}, new value={n2})',                       scope: 'SampleServer.Counter',           severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.5 },
      { body: 'Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]',                            scope: 'Microsoft.EntityFrameworkCore', severity: 'Debug', attachSpanIndex: 2, timeFraction: 0.7 },
      { body: 'Request finished HTTP/1.1 POST /counter/{value} 204 in {ms}ms',               scope: 'Microsoft.AspNetCore.Hosting',  severity: 'Info',  attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorLogs: [
      { body: 'Counter mutation accepted (delta={n}, new value={n2})',                       scope: 'SampleServer.Counter',           severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.4 },
      { body: 'Database deadlock detected during counter mutation; transaction aborted',     scope: 'Microsoft.EntityFrameworkCore', severity: 'Error', attachSpanIndex: 2, timeFraction: 0.7 },
      { body: 'Counter mutation failed: deadlock victim, retries exhausted',                 scope: 'SampleServer.Counter',           severity: 'Error', attachSpanIndex: 1, timeFraction: 0.85 },
      { body: 'Request finished HTTP/1.1 POST /counter/{value} 500 in {ms}ms',               scope: 'Microsoft.AspNetCore.Hosting',  severity: 'Error', attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorSpanIndex: 2
  }),
  withServices({
    id: 'post_counter_random',
    rootName: 'POST /counter/random',
    rootScope: 'Microsoft.AspNetCore',
    rootKind: 'Server',
    mu: 4.1, sigma: 0.55, weight: 22, errorRate: 0.05,
    errorMessage: 'Counter validation failed',
    children: [
      { name: 'counter.random',          scope: 'SampleServer.Counter', kind: 'Internal', startFraction: 0.05, durationFraction: 0.20 },
      { name: 'counter.mutate',          scope: 'SampleServer.Counter', kind: 'Internal', startFraction: 0.30, durationFraction: 0.25 },
      { name: 'pg.query UPDATE counter', scope: 'Npgsql',               kind: 'Client',   startFraction: 0.55, durationFraction: 0.35 }
    ],
    successLogs: [
      { body: 'Counter randomized to {n}',                                            scope: 'SampleServer.Counter',          severity: 'Info', attachSpanIndex: 1, timeFraction: 0.5 },
      { body: 'Counter mutation accepted (delta={n}, new value={n2})',                scope: 'SampleServer.Counter',          severity: 'Info', attachSpanIndex: 2, timeFraction: 0.5 },
      { body: 'Request finished HTTP/1.1 POST /counter/random 200 in {ms}ms',         scope: 'Microsoft.AspNetCore.Hosting',  severity: 'Info', attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorLogs: [
      { body: 'Counter value out of expected range (={n}); clamped',                  scope: 'SampleServer.Counter',          severity: 'Warn',  attachSpanIndex: 1, timeFraction: 0.6 },
      { body: 'Validation failed for counter mutation: value {n} exceeds max',        scope: 'SampleServer.Counter',          severity: 'Error', attachSpanIndex: 1, timeFraction: 0.85 },
      { body: 'Request finished HTTP/1.1 POST /counter/random 400 in {ms}ms',         scope: 'Microsoft.AspNetCore.Hosting',  severity: 'Warn',  attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorSpanIndex: 1
  }),
  withServices({
    id: 'post_counter_batch',
    rootName: 'POST /counter/batch',
    rootScope: 'Microsoft.AspNetCore',
    rootKind: 'Server',
    mu: 4.7, sigma: 0.55, weight: 6, errorRate: 0.06,
    errorMessage: 'Batch transaction rolled back',
    // Deep tree:
    //   root (0)
    //   ├── middleware.auth (1)
    //   ├── middleware.routing (2)
    //   └── handler.batch_post (3)
    //         ├── validator.batch (4)
    //         ├── repository.save_batch (5)
    //         │     ├── pg.BEGIN (6)
    //         │     ├── pg.UPDATE (7)
    //         │     └── pg.COMMIT (8)
    //         └── cache.invalidate (9)
    children: [
      { name: 'middleware.authentication', scope: 'OpenTelemetryDashboard.Auth',   kind: 'Internal', startFraction: 0.02, durationFraction: 0.04, parentIndex: 0 },
      { name: 'middleware.routing',        scope: 'Microsoft.AspNetCore.Routing',  kind: 'Internal', startFraction: 0.06, durationFraction: 0.03, parentIndex: 0 },
      { name: 'handler.batch_post',        scope: 'SampleServer.Handlers',         kind: 'Internal', startFraction: 0.10, durationFraction: 0.85, parentIndex: 0 },
      { name: 'validator.batch',           scope: 'SampleServer.Validation',       kind: 'Internal', startFraction: 0.13, durationFraction: 0.08, parentIndex: 3 },
      { name: 'repository.save_batch',     scope: 'SampleServer.Repositories',     kind: 'Internal', startFraction: 0.25, durationFraction: 0.55, parentIndex: 3 },
      { name: 'pg.BEGIN TRANSACTION',      scope: 'Npgsql',                        kind: 'Client',   startFraction: 0.27, durationFraction: 0.04, parentIndex: 5 },
      { name: 'pg.UPDATE counter (batch)', scope: 'Npgsql',                        kind: 'Client',   startFraction: 0.33, durationFraction: 0.40, parentIndex: 5 },
      { name: 'pg.COMMIT',                 scope: 'Npgsql',                        kind: 'Client',   startFraction: 0.75, durationFraction: 0.04, parentIndex: 5 },
      { name: 'cache.invalidate counter',  scope: 'StackExchange.Redis',           kind: 'Client',   startFraction: 0.85, durationFraction: 0.08, parentIndex: 3 }
    ],
    successLogs: [
      { body: 'Authenticated request via Bearer token (sub={n})',                                  scope: 'OpenTelemetryDashboard.Auth',  severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.5 },
      { body: 'Validated batch payload ({n} entries)',                                             scope: 'SampleServer.Validation',      severity: 'Info',  attachSpanIndex: 4, timeFraction: 0.5 },
      { body: 'Executed DbCommand ({ms}ms) [Parameters=[@p0=?]]',                                  scope: 'Microsoft.EntityFrameworkCore', severity: 'Debug', attachSpanIndex: 7, timeFraction: 0.6 },
      { body: 'Committed batch transaction ({n} rows)',                                            scope: 'SampleServer.Repositories',    severity: 'Info',  attachSpanIndex: 8, timeFraction: 0.7 },
      { body: 'Cache invalidated for {n} keys',                                                    scope: 'SampleServer.Cache',           severity: 'Info',  attachSpanIndex: 9, timeFraction: 0.5 },
      { body: 'Request finished HTTP/1.1 POST /counter/batch 200 in {ms}ms',                       scope: 'Microsoft.AspNetCore.Hosting', severity: 'Info',  attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorLogs: [
      { body: 'Authenticated request via Bearer token (sub={n})',                                  scope: 'OpenTelemetryDashboard.Auth',  severity: 'Info',  attachSpanIndex: 1, timeFraction: 0.5 },
      { body: 'Validated batch payload ({n} entries)',                                             scope: 'SampleServer.Validation',      severity: 'Info',  attachSpanIndex: 4, timeFraction: 0.5 },
      { body: 'Slow query detected ({ms}ms > 250ms threshold) — counter table',                    scope: 'SampleServer.Performance',     severity: 'Warn',  attachSpanIndex: 7, timeFraction: 0.6 },
      { body: 'Database deadlock detected during batch update; transaction rolled back',           scope: 'Microsoft.EntityFrameworkCore', severity: 'Error', attachSpanIndex: 7, timeFraction: 0.85 },
      { body: 'Repository.save_batch failed; surfacing 500 to caller',                             scope: 'SampleServer.Repositories',    severity: 'Error', attachSpanIndex: 5, timeFraction: 0.95 },
      { body: 'Request finished HTTP/1.1 POST /counter/batch 500 in {ms}ms',                       scope: 'Microsoft.AspNetCore.Hosting', severity: 'Error', attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorSpanIndex: 7
  }),
  withServices({
    id: 'healthcheck_redis',
    rootName: 'GET /healthz',
    rootScope: 'Microsoft.AspNetCore',
    rootKind: 'Server',
    mu: 2.5, sigma: 0.40, weight: 8, errorRate: 0.06,
    errorMessage: 'Redis probe timed out',
    children: [
      { name: 'redis.ping', scope: 'StackExchange.Redis', kind: 'Client', startFraction: 0.10, durationFraction: 0.80 }
    ],
    successLogs: [
      { body: "Health check 'redis' completed in {ms}ms", scope: 'Microsoft.Extensions.Diagnostics.HealthChecks', severity: 'Info', attachSpanIndex: 1, timeFraction: 0.7 }
    ],
    errorLogs: [
      { body: "Health check 'redis' failed: connection timeout after 2000ms", scope: 'Microsoft.Extensions.Diagnostics.HealthChecks', severity: 'Error', attachSpanIndex: 1, timeFraction: 0.8 },
      { body: 'Reporting unhealthy status to /healthz consumers',             scope: 'Microsoft.Extensions.Diagnostics.HealthChecks', severity: 'Warn',  attachSpanIndex: 0, timeFraction: 0.95 }
    ],
    errorSpanIndex: 1
  })
]

const TOTAL_WEIGHT = SCENARIOS.reduce((s, x) => s + x.weight, 0)

// ---- Trace-id encoding ---------------------------------------------------
// First 16 hex chars = startMs encoded; last 16 = entropy seed. Lets the
// detail page recover the trace's wall-clock time without a side-channel.

function encodeStartMs(ms: number): string {
  return Math.max(0, Math.floor(ms)).toString(16).padStart(16, '0')
}

function decodeStartMs(traceIdHex: string): number {
  const head = traceIdHex.slice(0, 16)
  const n = Number.parseInt(head, 16)
  return Number.isFinite(n) ? n : Date.now()
}

export function makeTraceId(startMs: number, entropySeed: number, index: number): string {
  const head = encodeStartMs(startMs)
  const tail = (((entropySeed ^ (index * 0x9e3779b9)) >>> 0).toString(16).padStart(8, '0')) +
               (((entropySeed * 16777619) ^ (index * 0xdeadbeef)) >>> 0).toString(16).padStart(8, '0')
  return head + tail
}

function makeSpanId(traceSeed: number, spanIndex: number): string {
  const a = ((traceSeed ^ (spanIndex * 0x9e3779b9)) >>> 0).toString(16).padStart(8, '0')
  const b = (((traceSeed * 0x85ebca6b) ^ (spanIndex * 0xc2b2ae35)) >>> 0).toString(16).padStart(8, '0')
  return a + b
}

// ---- Per-trace derivation -----------------------------------------------

export interface TracePlan {
  scenario: Scenario
  isError: boolean
  startMs: number
  durationMs: number
  /** Wall-clock end. */
  endMs: number
  /** Per-span: id, parentId (or null for root), startMs, durationMs. */
  spans: Array<{
    id: string
    parentId: string | null
    name: string
    scope: string
    kind: 'Server' | 'Client' | 'Internal'
    service: string
    startMs: number
    durationMs: number
    isError: boolean
  }>
}

/** Pick a scenario weighted by `weight`. Pure function over the rng. */
function pickScenarioByWeight(rand: () => number): Scenario {
  let roll = rand() * TOTAL_WEIGHT
  for (const s of SCENARIOS) {
    roll -= s.weight
    if (roll <= 0) return s
  }
  return SCENARIOS[SCENARIOS.length - 1]!
}

/**
 * Network overhead the sample-client Client span pads around the
 * sample-server Server span. Real HTTP roundtrips show DNS / TCP /
 * TLS / queue time on top of the server's processing, so the client
 * span is always strictly longer; we emulate that with a small fixed
 * delta on each side.
 */
const CLIENT_NET_OVERHEAD_MS = 3

/** Reconstruct the trace's full plan from a trace id alone. Used by
 *  both the detail and the log generators so they share spanIds and
 *  timestamps.
 *  <para>
 *  Every trace is wrapped in a synthetic <c>sample-client</c> Client
 *  span so the service map shows the realistic upstream caller —
 *  <c>sample-client → sample-server → (postgresql, redis)</c> — without
 *  duplicating the topology in every scenario.
 *  </para>
 */
export function planTrace(traceIdHex: string): TracePlan {
  const serverStartMs = decodeStartMs(traceIdHex)
  const seed = hashString(`scenario|${traceIdHex}`)
  const rand = mulberry32(seed)
  const scenario = pickScenarioByWeight(rand)
  const isError = rand() < scenario.errorRate
  const serverDurationMs = Math.max(1, Math.exp(scenario.mu + gaussian(rand) * scenario.sigma))

  const spanIdSeed = hashString(`span|${traceIdHex}`)

  // Synthetic outermost Client span on sample-client. Its window
  // brackets the server span with CLIENT_NET_OVERHEAD_MS slack on each
  // side to mirror network/queue latency a real HTTP roundtrip pays.
  const clientRootId = makeSpanId(spanIdSeed, 0)
  const clientStartMs = serverStartMs - CLIENT_NET_OVERHEAD_MS
  const clientDurationMs = serverDurationMs + 2 * CLIENT_NET_OVERHEAD_MS

  // Mirror the rootName as the client-facing operation. Strips the HTTP
  // route placeholders so the picker shows a clean op name.
  const clientOpName = scenario.rootName

  const serverSpanId = makeSpanId(spanIdSeed, 1)
  const spans: TracePlan['spans'] = [
    {
      id: clientRootId,
      parentId: null,
      name: clientOpName,
      scope: 'System.Net.Http',
      kind: 'Client',
      service: 'sample-client',
      startMs: clientStartMs,
      durationMs: clientDurationMs,
      isError
    },
    {
      id: serverSpanId,
      parentId: clientRootId,
      name: scenario.rootName,
      scope: scenario.rootScope,
      kind: scenario.rootKind,
      service: serviceFromScope(scenario.rootScope),
      startMs: serverStartMs,
      durationMs: serverDurationMs,
      isError
    }
  ]
  // Index 0 is the synthetic client root; index 1 is the scenario root
  // (the original "root" scenarios are authored against). Keep the
  // children's `parentIndex` semantics unchanged by inserting the
  // server span at logical index 0 of the scenario's tree.
  const ids: string[] = [serverSpanId]

  for (let c = 0; c < scenario.children.length; c++) {
    const child = scenario.children[c]!
    // c+2 because we already used 0 (client root) and 1 (server root).
    const childId = makeSpanId(spanIdSeed, c + 2)
    ids.push(childId)
    const parentIdx = child.parentIndex && child.parentIndex > 0 && child.parentIndex - 1 < c
      ? child.parentIndex
      : 0
    spans.push({
      id: childId,
      parentId: ids[parentIdx]!,
      name: child.name,
      scope: child.scope,
      kind: child.kind,
      service: serviceFromScope(child.scope),
      startMs: serverStartMs + child.startFraction * serverDurationMs,
      durationMs: child.durationFraction * serverDurationMs,
      isError: isError && c === scenario.errorSpanIndex
    })
  }

  return {
    scenario,
    isError,
    startMs: clientStartMs,
    durationMs: clientDurationMs,
    endMs: clientStartMs + clientDurationMs,
    spans
  }
}

/** Render a scenario's log script into LogRecordDtos for the given
 *  trace. Timestamps land inside the targeted span; trace+span ids
 *  match what `planTrace` returns so the trace detail page's alert
 *  markers line up.
 *  <para>
 *  Log scripts in <c>SCENARIOS</c> author span indices against the
 *  scenario's logical tree (root = 0, children[c] = c+1). After
 *  <c>planTrace</c> prepends the synthetic sample-client wrapper at
 *  index 0, those logical indices land one slot later in the
 *  reconstructed plan.spans array — we shift by +1 here so log
 *  attachment still matches the authored intent.
 *  </para>
 */
export function buildScenarioLogs(traceIdHex: string): LogRecordDto[] {
  const plan = planTrace(traceIdHex)
  const fillerSeed = hashString(`logfill|${traceIdHex}`)
  const fillerRand = mulberry32(fillerSeed)
  const script = plan.isError ? plan.scenario.errorLogs : plan.scenario.successLogs
  const out: LogRecordDto[] = []
  for (const line of script) {
    const idx = line.attachSpanIndex + 1
    if (idx < 0 || idx >= plan.spans.length) continue
    const span = plan.spans[idx]!
    const timeMs = span.startMs + span.durationMs * line.timeFraction
    out.push({
      time: new Date(timeMs).toISOString(),
      observedTime: new Date(timeMs).toISOString(),
      severityNumber: SEVERITY_NUMBER[line.severity],
      severityText: line.severity,
      body: fillTemplate(line.body, fillerRand),
      traceId: traceIdHex,
      spanId: span.id,
      scopeName: line.scope,
      scopeVersion: '1.0.0',
      resourceHash: `demo-${serviceFromScope(line.scope)}`,
      serviceName: serviceFromScope(line.scope),
      attributes: { 'demo.scenario': plan.scenario.id }
    })
  }
  return out
}

function fillTemplate(template: string, rand: () => number): string {
  return template
    .replace('{ms}', String(2 + Math.floor(rand() * 348)))
    .replace('{n2}', String(Math.floor(rand() * 1000)))
    .replace('{n}',  String(1 + Math.floor(rand() * 99)))
}

/** Convert a TracePlan into the DTO shape the trace-detail panel
 *  consumes. Kept here so `traces.ts` is just a thin wrapper. */
export function tracePlanToSpans(plan: TracePlan): SpanDto[] {
  return plan.spans.map(s => ({
    spanId: s.id,
    parentSpanId: s.parentId,
    name: s.name,
    kind: s.kind,
    start: new Date(s.startMs).toISOString(),
    end: new Date(s.startMs + s.durationMs).toISOString(),
    durationMs: Math.round(s.durationMs * 100) / 100,
    statusCode: s.isError ? 'Error' : 'Ok',
    statusMessage: s.isError ? plan.scenario.errorMessage : null,
    scopeName: s.scope,
    scopeVersion: '1.0.0',
    serviceName: s.service,
    attributes: {
      'demo.scenario': plan.scenario.id
    },
    events: [],
    links: []
  }))
}

/** Choose a random scenario for a given service filter. Returns null
 *  if no scenario touches that service. */
export function scenariosForService(service: string | null | undefined): Scenario[] {
  if (!service) return SCENARIOS
  return SCENARIOS.filter(s => s.services.includes(service))
}
