import type { LogRecordDto, SpanDto } from '~/services/types'
import { severityBucketFromNumber, type SeverityBucket } from '~/types/filters'

export type AlertBucket = Extract<SeverityBucket, 'warn' | 'error' | 'fatal'>

export interface LogMarker {
  /** Position [0,1] within the trace timeline (same coord system as
   *  the span's `offset`). Both views reuse this — span-tree paints
   *  it on top of the bar, flame paints it inside the span box. */
  position: number
  bucket: AlertBucket
  body: string
  time: string
  /** Stable key for v-for. */
  key: string
}

export interface PositionedSpan {
  span: SpanDto
  /** Tree depth (root = 0), used as the lane index in the flame view
   *  and as left-padding rungs in the tree view. */
  depth: number
  /** Span start as fraction of the trace timeline [0,1]. */
  offset: number
  /** Span duration as fraction of the trace timeline [0,1]; floored
   *  to a thin sliver so very short spans stay clickable. */
  width: number
  alerts: LogMarker[]
}

export interface TraceLayout {
  spans: PositionedSpan[]
  /** Inclusive trace start / end in epoch-ms; `durationMs` is the span
   *  the offsets/widths above normalise against. */
  traceStartMs: number
  traceEndMs: number
  durationMs: number
  maxDepth: number
}

/**
 * Pure layout calculation shared by `SpanTree` and `SpanFlameGraph`.
 * Both views need the same depth/offset/width/alerts triple per span;
 * only the rendering shape (linear list vs. depth-stacked timeline)
 * differs. Keeping this in a composable means a fix to (e.g.) cycle
 * detection or alert filtering applies to both at once.
 */
export function buildTraceLayout(spans: SpanDto[], logs?: LogRecordDto[]): TraceLayout {
  if (spans.length === 0) {
    return { spans: [], traceStartMs: 0, traceEndMs: 0, durationMs: 0, maxDepth: 0 }
  }

  const byId = new Map<string, SpanDto>()
  for (const s of spans) byId.set(s.spanId, s)

  const traceStartMs = spans.reduce(
    (min, s) => {
      const t = new Date(s.start).getTime()
      return t < min ? t : min
    },
    Number.POSITIVE_INFINITY
  )
  const traceEndMs = spans.reduce(
    (max, s) => {
      const t = new Date(s.end).getTime()
      return t > max ? t : max
    },
    Number.NEGATIVE_INFINITY
  )
  const durationMs = Math.max(1, traceEndMs - traceStartMs)

  const depthCache = new Map<string, number>()
  function depthOf(s: SpanDto, guard = 0): number {
    if (guard > 64) return 0 // pathological cycle guard
    const cached = depthCache.get(s.spanId)
    if (cached !== undefined) return cached
    if (!s.parentSpanId) {
      depthCache.set(s.spanId, 0)
      return 0
    }
    const parent = byId.get(s.parentSpanId)
    const d = parent ? depthOf(parent, guard + 1) + 1 : 0
    depthCache.set(s.spanId, d)
    return d
  }

  // Group warn/error/fatal logs by their owning span. Lower-severity
  // entries fall through (info/debug aren't rendered as alert markers).
  const alertsBySpanId = new Map<string, LogMarker[]>()
  for (const log of logs ?? []) {
    if (!log.spanId) continue
    const bucket = severityBucketFromNumber(log.severityNumber)
    if (bucket !== 'warn' && bucket !== 'error' && bucket !== 'fatal') continue
    const arr = alertsBySpanId.get(log.spanId) ?? []
    arr.push({
      position: (new Date(log.time).getTime() - traceStartMs) / durationMs,
      bucket,
      body: log.body ?? '',
      time: log.time,
      key: `${log.spanId}|${log.time}|${(log.body ?? '').slice(0, 32)}`
    })
    alertsBySpanId.set(log.spanId, arr)
  }

  let maxDepth = 0
  const positioned = [...spans]
    .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
    .map(s => {
      const start = new Date(s.start).getTime()
      const end = new Date(s.end).getTime()
      const depth = depthOf(s)
      if (depth > maxDepth) maxDepth = depth
      return {
        span: s,
        depth,
        offset: (start - traceStartMs) / durationMs,
        width: Math.max(0.005, (end - start) / durationMs),
        alerts: alertsBySpanId.get(s.spanId) ?? []
      }
    })

  return { spans: positioned, traceStartMs, traceEndMs, durationMs, maxDepth }
}
