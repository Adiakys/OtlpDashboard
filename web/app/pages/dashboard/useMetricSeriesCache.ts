import type { MetricsService } from '~/services/MetricsService'
import type { InstrumentRef, MetricSeriesDto, TimeWindow } from '~/services/types'
import { STATE_SERIES_CACHE } from './composables/stateKeys'

/**
 * Page-scoped dedup cache for metric points. Multiple widgets binding the
 * same instrument on the same range collapse into a single network request
 * per live tick, instead of fanning out N parallel GETs.
 *
 * Cache key: `<resourceHash>|<scope>|<name>|<kind>|<from>|<to>|<attrs>` — ranges
 * that differ by even a millisecond miss the cache (intentional: live mode
 * passes a fresh `now` every tick, and we want each tick's window to be
 * honoured). `attrs` is a boolean flag so a Stat widget caching the slim
 * payload doesn't poison a Line widget that needs the per-point attributes.
 *
 * Inflight requests are awaited rather than restarted; on success the result
 * is parked in the cache for `TTL_MS` so back-to-back widget mounts on the
 * same data don't re-fetch.
 */

interface CacheEntry {
  data: MetricSeriesDto
  fetchedAt: number
}

interface CacheState {
  entries: Map<string, CacheEntry>
}

const TTL_MS = 4_500 // just under the 5s default live tick

const inflight = new Map<string, Promise<MetricSeriesDto>>()

export function useMetricSeriesCache(metrics: MetricsService) {
  const state = useState<CacheState>(STATE_SERIES_CACHE, () => ({
    entries: new Map<string, CacheEntry>()
  }))

  function keyFor(ref: InstrumentRef, window: TimeWindow, includeAttributes: boolean): string {
    return `${ref.resourceHash}|${ref.scopeName}|${ref.instrumentName}|${ref.kind}|${window.from}|${window.to}|${includeAttributes ? 'a' : ''}`
  }

  /**
   * Fetch (or return cached) points for the given instrument + window. The
   * `bypassCache` flag forces a network round-trip — used when the page-level
   * live tick wants every widget to see fresh data even if the cache TTL
   * hasn't elapsed yet. `includeAttributes` mirrors the server-side opt-in:
   * pass `true` only when the consumer needs the per-point attribute map.
   */
  function getPoints(
    ref: InstrumentRef,
    window: TimeWindow,
    options: { bypassCache?: boolean; includeAttributes?: boolean } = {}
  ): Promise<MetricSeriesDto> {
    const includeAttributes = options.includeAttributes === true
    const bypassCache = options.bypassCache === true
    const key = keyFor(ref, window, includeAttributes)
    const now = Date.now()
    const cached = state.value.entries.get(key)
    if (!bypassCache && cached && now - cached.fetchedAt < TTL_MS) {
      return Promise.resolve(cached.data)
    }
    const pending = inflight.get(key)
    if (pending) return pending

    const promise = metrics.getPoints({
      resourceHash: ref.resourceHash,
      scopeName: ref.scopeName,
      instrumentName: ref.instrumentName,
      kind: ref.kind,
      from: window.from,
      to: window.to,
      includeAttributes
    }).then(data => {
      state.value.entries.set(key, { data, fetchedAt: Date.now() })
      // Bound the cache: drop entries older than 2× TTL so long-lived sessions
      // don't accumulate unbounded keys (every live tick is a new window).
      const cutoff = Date.now() - TTL_MS * 2
      for (const [k, v] of state.value.entries) {
        if (v.fetchedAt < cutoff) state.value.entries.delete(k)
      }
      return data
    }).finally(() => {
      inflight.delete(key)
    })

    inflight.set(key, promise)
    return promise
  }

  /** Forget every cached entry. Called on live tick so widgets that share an
   *  instrument all hit the network once and then read from the warm cache. */
  function invalidate(): void {
    state.value.entries.clear()
  }

  return { getPoints, invalidate }
}
