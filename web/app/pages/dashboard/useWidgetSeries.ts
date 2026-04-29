import type { ComputedRef } from 'vue'
import type { MetricsService } from '~/services/MetricsService'
import type { MetricSeriesDto, TimeWindow } from '~/services/types'
import type { MetricBinding, RangePreset } from './types'
import { useInstrumentCatalog } from './useInstrumentCatalog'
import { useMetricSeriesCache } from './useMetricSeriesCache'

/** RangePreset → TimeWindow anchored at `now`. */
export function presetToWindow(preset: RangePreset, now: number = Date.now()): TimeWindow {
  const minute = 60 * 1000
  const hour = 60 * minute
  const durationByPreset: Record<RangePreset, number> = {
    'last-5m': 5 * minute,
    'last-15m': 15 * minute,
    'last-1h': hour,
    'last-6h': 6 * hour,
    'last-24h': 24 * hour
  }
  const duration = durationByPreset[preset]
  return {
    from: new Date(now - duration).toISOString(),
    to: new Date(now).toISOString()
  }
}

/**
 * Loads MetricSeriesDto[] for a list of bindings on a sliding range, with
 * automatic re-fetch on:
 *  - bindings change
 *  - range change
 *  - `liveTick` change (the page bumps it on every live polling tick)
 *
 * Two cross-widget concerns are delegated:
 *  - resourceHash resolution → `useInstrumentCatalog` (late binding by logical key)
 *  - request dedup → `useMetricSeriesCache` (shared per-page cache so 5 widgets
 *    on the same metric do 1 GET, not 5)
 *
 * `liveTick` is taken as a getter, not a Ref: Vue auto-unwraps refs when they
 * cross the props boundary, so a widget passing `props.liveTick` would hand
 * us a primitive `number` and `watch` would never fire. A getter
 * (`() => props.liveTick`) keeps the dependency reactive on the prop itself.
 */
export function useWidgetSeries(
  service: MetricsService,
  metrics: ComputedRef<MetricBinding[]>,
  range: ComputedRef<RangePreset>,
  liveTick: () => number
) {
  const catalog = useInstrumentCatalog(service)
  const cache = useMetricSeriesCache(service)

  const series = ref<MetricSeriesDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  // Tracks whether a fetch has ever completed for the current binding set.
  // Widgets use it to switch from "skeleton" to "no data".
  const hasLoaded = ref(false)

  let inFlight = 0

  async function load() {
    const bindings = metrics.value
    if (bindings.length === 0) {
      series.value = []
      error.value = null
      hasLoaded.value = true
      return
    }

    const ticket = ++inFlight
    loading.value = true
    error.value = null

    await catalog.ensureLoaded()
    if (ticket !== inFlight) return

    const window = presetToWindow(range.value)
    try {
      const results = await Promise.all(bindings.map(b => fetchSeries(b, window)))
      if (ticket !== inFlight) return
      series.value = results.filter((r): r is MetricSeriesDto => r !== null)
      hasLoaded.value = true
    } catch (e) {
      if (ticket === inFlight) {
        error.value = e instanceof Error ? e.message : String(e)
      }
    } finally {
      if (ticket === inFlight) loading.value = false
    }
  }

  async function fetchSeries(binding: MetricBinding, window: TimeWindow): Promise<MetricSeriesDto | null> {
    const resolved = catalog.resolve(binding)
    if (!resolved) return null
    return cache.getPoints({
      resourceHash: resolved.resourceHash,
      scopeName: resolved.scopeName,
      instrumentName: resolved.instrumentName,
      kind: resolved.kind
    }, window)
  }

  watch(metrics, load, { immediate: true, deep: true })
  watch(range, load)
  watch(liveTick, load)

  return { series, loading, error, hasLoaded, reload: load }
}
