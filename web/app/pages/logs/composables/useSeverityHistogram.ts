import type { ComputedRef, Ref } from 'vue'
import type { LogRecordDto, TimeWindow } from '~/services/types'
import { severityBucketFromNumber, type SeverityBucket } from '~/types/filters'

/**
 * Wire shape consumed by <c>LogsSeverityHistogram.vue</c> — agnostic of
 * how the data was produced. Today the page wires it up with
 * <see cref="useInMemoryLogsHistogram"/>, which buckets whatever logs
 * are already on the screen; a future <c>useApiLogsHistogram</c> can
 * call a server-side aggregation endpoint and yield the same shape
 * without touching the renderer.
 */
export interface SeverityHistogramData {
  /** Time-ordered, equal-width buckets that span the configured window. */
  buckets: SeverityHistogramBucket[]
  /** Total log count across all buckets — handy for the header label. */
  total: number
  /** True when the source can't see every log in the window (e.g. the
   *  in-memory source hit the page limit). The renderer surfaces this
   *  as a small "showing latest N" footnote so users know the volumes
   *  may be capped. */
  truncated: boolean
}

export interface SeverityHistogramBucket {
  /** Bucket start / end in epoch-ms (closed-open: <c>[startMs, endMs)</c>). */
  startMs: number
  endMs: number
  /** Per-severity-bucket counts. The renderer stacks these in the
   *  order chosen by <see cref="STACK_ORDER"/>. Severities not present
   *  in the data are absent from the map (callers default to 0). */
  counts: Partial<Record<SeverityBucket, number>>
  total: number
}

/**
 * Stacking order from bottom to top of the column. Lower-severity rows
 * sit at the bottom so the eye-catching warn/error/fatal stripes float
 * on top — exactly where you want them when scanning for spikes.
 */
export const STACK_ORDER: readonly SeverityBucket[] = [
  'trace', 'debug', 'info', 'warn', 'error', 'fatal'
]

const DEFAULT_BUCKET_COUNT = 30

export interface UseInMemoryLogsHistogramOptions {
  /** Override the default 30 columns. Mostly for unit tests. */
  bucketCount?: number
}

/**
 * Compute the histogram from logs already in memory. Suited for the
 * common case where the page-cap covers the configured window
 * (default limit 50 · last-1h on the demo · plenty for typical
 * traffic). On overflow the <c>truncated</c> flag in the result lets
 * the renderer disclose the limitation honestly.
 *
 * The composable contract — <c>(Ref&lt;…&gt;) =&gt; ComputedRef&lt;SeverityHistogramData&gt;</c> —
 * mirrors what an API-backed version would expose, so swapping in a
 * server source is a one-line change at the call site.
 */
export function useInMemoryLogsHistogram(
  logs: Ref<readonly LogRecordDto[]>,
  range: Ref<TimeWindow>,
  truncated: Ref<boolean>,
  options: UseInMemoryLogsHistogramOptions = {}
): ComputedRef<SeverityHistogramData> {
  const bucketCount = Math.max(2, options.bucketCount ?? DEFAULT_BUCKET_COUNT)

  return computed<SeverityHistogramData>(() => {
    const fromMs = new Date(range.value.from).getTime()
    const toMs = new Date(range.value.to).getTime()
    const windowMs = Math.max(1, toMs - fromMs)
    const stride = windowMs / bucketCount

    const buckets: SeverityHistogramBucket[] = []
    for (let i = 0; i < bucketCount; i++) {
      const startMs = fromMs + i * stride
      buckets.push({
        startMs,
        endMs: i === bucketCount - 1 ? toMs : fromMs + (i + 1) * stride,
        counts: {},
        total: 0
      })
    }

    let total = 0
    for (const log of logs.value) {
      const t = new Date(log.time).getTime()
      // Drop entries outside the window — the in-memory list can
      // legitimately contain rows just past the boundary during a
      // live tail and we don't want them to inflate the last bucket.
      if (t < fromMs || t > toMs) continue
      const idx = t === toMs
        ? bucketCount - 1
        : Math.min(bucketCount - 1, Math.max(0, Math.floor((t - fromMs) / stride)))
      const bucket = buckets[idx]!
      const sev = severityBucketFromNumber(log.severityNumber)
      bucket.counts[sev] = (bucket.counts[sev] ?? 0) + 1
      bucket.total++
      total++
    }

    return { buckets, total, truncated: truncated.value }
  })
}
