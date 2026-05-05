import type { LogRecordDto, PagedResponse } from '~/services/types'
import { hashString, mulberry32, pickWeighted } from './prng'
import { buildScenarioLogs, recentTraceIds } from './traces'

/**
 * Log generator — coherent with the scenario-driven trace generators.
 * Three sources feed the result:
 *
 *  1. `traceIdFilter` set → the scenario-correlated logs for that
 *     trace exclusively. Same shape the trace-detail page would
 *     reconstruct via `buildScenarioLogs`, so the "view logs" CTA
 *     lands on a tightly-focused list.
 *  2. Otherwise → a mix of scenario logs (for a deterministic set of
 *     "recent" traces inside the window) and standalone background
 *     logs. The scenario logs carry real trace+span ids that match
 *     what `generateTraceDetail` will produce; the background ones
 *     are purely lifecycle / GC / healthcheck noise.
 *
 * Severity / service filters are applied after the mix is built so
 * the densities stay realistic.
 */

interface BackgroundLogTemplate {
  service: string
  scope: string
  severity: 'Debug' | 'Info' | 'Warn' | 'Error'
  body: string
  weight: number
}

const SEVERITY_NUMBER: Record<BackgroundLogTemplate['severity'], number> = {
  Debug: 5, Info: 9, Warn: 13, Error: 17
}

const BACKGROUND_TEMPLATES: BackgroundLogTemplate[] = [
  { service: 'sample-server', scope: 'OpenTelemetryDashboard.Retention',  severity: 'Info',  body: "Hosted service 'TelemetryRetentionWorker' running sweep cycle",        weight: 10 },
  { service: 'sample-server', scope: 'Runtime',                            severity: 'Debug', body: 'GC freeing {n}MB heap (gen2)',                                        weight: 12 },
  { service: 'sample-server', scope: 'Microsoft.Hosting.Lifetime',         severity: 'Info',  body: 'Application started. Listening on http://[::]:8080',                  weight:  2 },
  { service: 'sample-server', scope: 'Microsoft.Extensions.Configuration', severity: 'Info',  body: 'Configuration reloaded: {n} keys changed',                            weight:  3 },
  { service: 'sample-server', scope: 'SampleServer.Queue',                 severity: 'Debug', body: 'Background queue depth = {n}',                                        weight:  8 },
  { service: 'sample-server', scope: 'Runtime',                            severity: 'Warn',  body: 'Slow GC pause detected: {ms}ms (gen2)',                               weight:  2 },
  { service: 'postgresql',    scope: 'Npgsql',                             severity: 'Warn',  body: 'Connection pool exhaustion approaching: {n}/100 in use',              weight:  1 },
  { service: 'postgresql',    scope: 'postgres',                           severity: 'Info',  body: 'duration: {ms} ms  statement: SELECT id FROM ledger WHERE …',         weight:  4 },
  { service: 'postgresql',    scope: 'postgres',                           severity: 'Warn',  body: 'autovacuum: VACUUM public.counter (took {ms} ms)',                    weight:  2 },
  { service: 'redis',         scope: 'redis-server',                       severity: 'Info',  body: '{n} clients connected, used_memory_human=12.4M',                      weight:  3 },
  { service: 'redis',         scope: 'redis-server',                       severity: 'Warn',  body: 'Possible RDB save lag — last save {n}s ago',                          weight:  1 },
  { service: 'sample-client', scope: 'Demo.Client.Worker',                 severity: 'Info',  body: 'Polled queue, dequeued {n} jobs',                                     weight:  6 }
]

function fillTemplate(template: string, rand: () => number): string {
  return template
    .replace('{ms}', String(2 + Math.floor(rand() * 348)))
    .replace('{n2}', String(Math.floor(rand() * 1000)))
    .replace('{n}',  String(1 + Math.floor(rand() * 99)))
}

/**
 * Generate the log stream for the given window. See module docstring
 * for the source mix.
 */
export function generateLogList(args: {
  fromMs: number
  toMs: number
  limit: number
  service?: string | null
  minSeverity?: number
  traceIdFilter?: string | null
}): PagedResponse<LogRecordDto> {
  // Scoped-down case: a specific trace was requested. Return only its
  // scripted logs — that's what the trace-detail "view logs" link is
  // asking for.
  if (args.traceIdFilter) {
    const items = buildScenarioLogs(args.traceIdFilter)
      .filter(l => passesSeverity(l, args.minSeverity))
      .filter(l => !args.service || l.serviceName === args.service)
      .filter(l => withinWindow(l, args.fromMs, args.toMs))
      .sort((a, b) => b.time.localeCompare(a.time))
      .slice(0, args.limit)
    return { items, nextCursor: null }
  }

  const seed = hashString(`logs|${args.fromMs}|${args.toMs}|${args.service ?? ''}`)
  const rand = mulberry32(seed)
  const traceSeed = hashString(`traces|${args.fromMs}|${args.toMs}|${args.service ?? ''}`)

  // Match the trace-list density so a window with N traces also
  // surfaces the corresponding scenario logs (~3 per trace on
  // average).
  const windowSec = (args.toMs - args.fromMs) / 1000
  const traceCount = Math.max(8, Math.floor(windowSec / 6))
  const traceIds = recentTraceIds(traceSeed, args.fromMs, args.toMs, traceCount)

  const out: LogRecordDto[] = []

  // 1. Scenario logs for each "recent" trace.
  for (const tid of traceIds) {
    for (const log of buildScenarioLogs(tid)) {
      if (!withinWindow(log, args.fromMs, args.toMs)) continue
      out.push(log)
    }
  }

  // 2. Background noise — fill up to roughly the same volume as the
  //    scenario-correlated stream so the page isn't 100% trace-tied.
  const backgroundTarget = out.length
  const weighted = BACKGROUND_TEMPLATES.map(t => ({ value: t, weight: t.weight }))
  for (let i = 0; i < backgroundTarget; i++) {
    const tpl = pickWeighted(rand, weighted)
    const t = args.fromMs + Math.floor(rand() * Math.max(1, args.toMs - args.fromMs))
    out.push({
      time: new Date(t).toISOString(),
      observedTime: new Date(t).toISOString(),
      severityNumber: SEVERITY_NUMBER[tpl.severity],
      severityText: tpl.severity,
      body: fillTemplate(tpl.body, rand),
      traceId: null,
      spanId: null,
      scopeName: tpl.scope,
      scopeVersion: '1.0.0',
      resourceHash: `demo-${tpl.service}`,
      serviceName: tpl.service,
      attributes: { 'demo.scenario': 'background' }
    })
  }
  // Filters + sort + cap.
  const filtered = out
    .filter(l => passesSeverity(l, args.minSeverity))
    .filter(l => !args.service || l.serviceName === args.service)
    .sort((a, b) => b.time.localeCompare(a.time))
    .slice(0, args.limit)

  return { items: filtered, nextCursor: null }
}

function passesSeverity(l: LogRecordDto, minSeverity: number | undefined): boolean {
  if (minSeverity == null || minSeverity <= 0) return true
  return l.severityNumber >= minSeverity
}

function withinWindow(l: LogRecordDto, fromMs: number, toMs: number): boolean {
  const t = new Date(l.time).getTime()
  return t >= fromMs && t <= toMs
}
