import type { MetricPointDto, MetricSeriesDto } from '~/services/types'
import { gaussian, hashString, mulberry32 } from './prng'
import type { InstrumentSpec } from '../data/instruments'

const POINT_INTERVAL_MS = 30_000

/**
 * Generate a `MetricSeriesDto` for one instrument over the requested
 * window. Random-walk: `value[t] = clamp(value[t-1] + N(drift, jitter))`,
 * seeded by `(scope, name, kind, service)` so the same instrument gives
 * the same shape on every refresh. Cumulative `Sum` instruments add a
 * monotonic offset that grows with `t-startOfWindow` — a real cumulative
 * counter has been ticking up since the process started, so the visible
 * portion is a slice of a much larger absolute value.
 *
 * If the spec declares `splitBy`, the function emits one walk per
 * attribute value. When `includeAttributes` is true the points carry
 * the full `{ attr: value }` map; otherwise points are summed across
 * splits so the shape collapses to a single time-series with empty
 * attributes (matches the server's default opt-out).
 */
export function generateMetricSeries(
  spec: InstrumentSpec,
  fromMs: number,
  toMs: number,
  includeAttributes: boolean
): MetricSeriesDto {
  const points: MetricPointDto[] = []
  const startTime = new Date(fromMs).toISOString()

  const seed = hashString(
    `${spec.dto.scopeName}|${spec.dto.name}|${spec.dto.kind}|${spec.dto.serviceName}`
  )

  if (spec.splitBy) {
    const seriesPerSplit = spec.splitBy.values.map((v) => ({
      attrValue: v.value,
      walk: walk(spec, fromMs, toMs, mulberry32(seed ^ hashString(v.value)), {
        baseline: v.baseline,
        drift: v.drift ?? spec.drift
      })
    }))

    if (includeAttributes) {
      for (const s of seriesPerSplit) {
        for (const p of s.walk) {
          points.push({
            time: p.time,
            startTime,
            value: p.value,
            attributes: { [spec.splitBy.attr]: s.attrValue }
          })
        }
      }
    } else {
      const len = seriesPerSplit[0]?.walk.length ?? 0
      for (let i = 0; i < len; i++) {
        const time = seriesPerSplit[0]!.walk[i]!.time
        const sum = seriesPerSplit.reduce((s, ser) => s + (ser.walk[i]?.value ?? 0), 0)
        points.push({ time, startTime, value: sum, attributes: {} })
      }
    }
  } else {
    const walked = walk(spec, fromMs, toMs, mulberry32(seed), {
      baseline: spec.baseline,
      drift: spec.drift
    })
    for (const p of walked) {
      points.push({ time: p.time, startTime, value: p.value, attributes: {} })
    }
  }

  return {
    instrument: spec.dto,
    points,
    truncated: false
  }
}

function walk(
  spec: InstrumentSpec,
  fromMs: number,
  toMs: number,
  rand: () => number,
  initial: { baseline: number; drift: number }
): { time: string; value: number }[] {
  const out: { time: string; value: number }[] = []
  const monotonic = spec.dto.kind === 'Sum' && spec.dto.isMonotonic

  // Cumulative counters carry a base offset proportional to wall-clock so
  // the absolute number looks like a long-running counter, not a counter
  // that just got reset to zero at the window start.
  const baseOffset = monotonic
    ? Math.floor((fromMs / POINT_INTERVAL_MS) * Math.max(initial.drift, 0))
    : 0

  let cur = initial.baseline + baseOffset
  let firstStep = true

  for (let t = fromMs; t <= toMs; t += POINT_INTERVAL_MS) {
    if (firstStep) {
      firstStep = false
    } else {
      const step = initial.drift + gaussian(rand) * spec.jitter
      cur = monotonic ? cur + Math.max(0, step) : cur + step
    }
    if (spec.min !== undefined) cur = Math.max(spec.min, cur)
    if (spec.max !== undefined) cur = Math.min(spec.max, cur)
    out.push({ time: new Date(t).toISOString(), value: round(cur, spec.dto.unit) })
  }
  return out
}

function round(value: number, unit: string | null): number {
  // Counts and coarse units stay integer; everything else keeps two decimals.
  if (
    unit === '{thread}' ||
    unit === '{exception}' ||
    unit === '{collection}' ||
    unit === '{contention}' ||
    unit === '{connection}' ||
    unit === '{deadlock}' ||
    unit === '{database}' ||
    unit === '{table}' ||
    unit === '{transaction}'
  ) {
    return Math.round(value)
  }
  if (unit === 'By') return Math.round(value)
  return Math.round(value * 100) / 100
}
