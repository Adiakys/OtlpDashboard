import type {
  PagedResponse,
  TraceDetailDto,
  TraceSummaryDto
} from '~/services/types'
import { DEMO_SERVICES } from '../data/services'
import { hashString, mulberry32 } from './prng'
import {
  buildScenarioLogs,
  makeTraceId,
  planTrace,
  scenariosForService,
  tracePlanToSpans,
  SCENARIOS,
  type Scenario
} from './scenarios'

/**
 * Trace generators — every trace is built from a Scenario (see
 * `scenarios.ts`). The trace id encodes its own start time so the
 * detail page reconstructs the same wall-clock the list showed, and
 * the per-scenario log script (in scenarios.ts) drives both the alert
 * markers on the span bar and the correlated entries in the logs
 * page. List, detail, and logs are now genuinely consistent: a given
 * trace id always produces the same spans and the same log lines,
 * with the same severities targeting the same spans.
 */

function pickScenarioByWeight(scenarios: Scenario[], rand: () => number): Scenario {
  const total = scenarios.reduce((s, x) => s + x.weight, 0)
  let roll = rand() * total
  for (const s of scenarios) {
    roll -= s.weight
    if (roll <= 0) return s
  }
  return scenarios[scenarios.length - 1]!
}

/**
 * Generate a paged list of trace summaries for the requested window.
 * Density is tied to the window size, capped by limit.
 */
export function generateTraceList(args: {
  fromMs: number
  toMs: number
  limit: number
  service?: string | null
  cursor?: string | null
}): PagedResponse<TraceSummaryDto> {
  const windowSec = (args.toMs - args.fromMs) / 1000
  const totalTraces = Math.min(args.limit, Math.max(8, Math.floor(windowSec / 6)))
  const seed = hashString(`traces|${args.fromMs}|${args.toMs}|${args.service ?? ''}`)
  const rand = mulberry32(seed)

  const candidates = scenariosForService(args.service)
  if (candidates.length === 0) return { items: [], nextCursor: null }

  const items: TraceSummaryDto[] = []
  for (let i = 0; i < totalTraces; i++) {
    const scenario = pickScenarioByWeight(candidates, rand)
    // Pick a start time inside the requested window. The scenario's
    // own duration is sampled from its lognormal in `planTrace`, but
    // we need the duration *here* to know where the trace ends — so
    // we compute startMs first from the window and let `planTrace`
    // recover it from the trace id.
    const startMs = args.fromMs + Math.floor(rand() * Math.max(1, args.toMs - args.fromMs - 1))
    const traceId = makeTraceId(startMs, seed, i)
    const plan = planTrace(traceId)
    // Surface the multi-service shape of the scenario in the same
    // form the real backend ships: root's service excluded, others
    // sorted for deterministic tooltips.
    const rootService = plan.spans[0]?.service ?? 'sample-server'
    const otherServiceNames = [...new Set(
      plan.spans.map(s => s.service).filter(s => s && s !== rootService)
    )].sort()
    items.push({
      traceId,
      rootSpanName: scenario.rootName,
      start: new Date(plan.startMs).toISOString(),
      end: new Date(plan.endMs).toISOString(),
      durationMs: Math.round(plan.durationMs * 100) / 100,
      spanCount: plan.spans.length,
      rootStatusCode: plan.isError ? 'Error' : 'Ok',
      resourceHash: `demo-${rootService}`,
      serviceName: rootService,
      otherServiceNames
    })
  }
  items.sort((a, b) => b.start.localeCompare(a.start))
  return { items, nextCursor: null }
}

/**
 * Reconstruct the deterministic trace detail for any traceId — same
 * spans, same statuses, same timing as the list. Logs for the trace
 * (`buildScenarioLogs`) target span ids from the same plan, so alert
 * markers on the span bar line up with the actual span lanes.
 */
export function generateTraceDetail(traceIdHex: string): TraceDetailDto {
  const plan = planTrace(traceIdHex)
  return { traceId: traceIdHex, spans: tracePlanToSpans(plan), truncated: false }
}

/** Used by the logs generator to tail a window with scenario-correlated
 *  entries. Each id deterministically encodes a start time inside the
 *  window. */
export function recentTraceIds(seed: number, fromMs: number, toMs: number, count: number): string[] {
  const rand = mulberry32(seed)
  const out: string[] = []
  const range = Math.max(1, toMs - fromMs - 1)
  for (let i = 0; i < count; i++) {
    const startMs = fromMs + Math.floor(rand() * range)
    out.push(makeTraceId(startMs, seed, i))
  }
  return out
}

/** Re-export `buildScenarioLogs` so the logs generator can pull it
 *  from the traces module without a deeper import chain. */
export { buildScenarioLogs }

export { DEMO_SERVICES as TRACE_DEMO_SERVICES, SCENARIOS as DEMO_SCENARIOS }
