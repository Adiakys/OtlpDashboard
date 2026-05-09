import type { ComputedRef } from 'vue'
import type { MetricsService } from '~/services/MetricsService'
import type { MetricSeriesDto, TimeWindow } from '~/services/types'
import type { MetricBinding, RangePreset } from './types'
import { useInstrumentCatalog, type Resolution } from './useInstrumentCatalog'
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
 * Diagnostic surfaced to the widget chrome when a binding can't be
 * resolved unambiguously. The widget renders a non-blocking warning
 * chip ("Multiple instances of `<service>` — pick one"); data fetch
 * is skipped for that binding so the widget doesn't display arbitrary
 * data from one of several instances.
 */
export interface ResolutionWarning {
  serviceName: string | null
  /** The id the user pinned that's missing now, or null when no pin was set. */
  requestedId: string | null
  /** Instance ids the user can choose from in the config form. */
  available: string[]
}

/**
 * Loads MetricSeriesDto[] for a list of bindings on a sliding range, with
 * automatic re-fetch on:
 *  - bindings change
 *  - range change
 *  - `liveTick` change (the page bumps it on every live polling tick)
 *
 * Two cross-widget concerns are delegated:
 *  - resourceHash resolution → `useInstrumentCatalog` (late binding by
 *    logical key + optional `service.instance.id`)
 *  - request dedup → `useMetricSeriesCache` (shared per-page cache so 5
 *    widgets on the same metric do 1 GET, not 5)
 *
 * Ambiguous bindings (multiple instances match without a pin, or pinned
 * id missing) are surfaced via `warnings[]` and skipped during fetch —
 * widgets show a chip rather than data picked from an arbitrary instance.
 *
 * `liveTick` is taken as a getter, not a Ref: Vue auto-unwraps refs when
 * they cross the props boundary, so a widget passing `props.liveTick`
 * would hand us a primitive `number` and `watch` would never fire. A
 * getter (`() => props.liveTick`) keeps the dependency reactive.
 */
export function useWidgetSeries(
  service: MetricsService,
  metrics: ComputedRef<MetricBinding[]>,
  range: ComputedRef<RangePreset>,
  liveTick: () => number,
  options: { includeAttributes?: boolean } = {}
) {
  const includeAttributes = options.includeAttributes === true
  const catalog = useInstrumentCatalog(service)
  const cache = useMetricSeriesCache(service)

  const series = ref<MetricSeriesDto[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const warnings = ref<ResolutionWarning[]>([])
  // Tracks whether a fetch has ever completed for the current binding set.
  // Widgets use it to switch from "skeleton" to "no data".
  const hasLoaded = ref(false)

  let inFlight = 0

  async function load() {
    const bindings = metrics.value
    if (bindings.length === 0) {
      series.value = []
      warnings.value = []
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
      const fetched: Array<MetricSeriesDto | null> = []
      const collectedWarnings: ResolutionWarning[] = []
      for (const binding of bindings) {
        const result = await fetchSeries(binding, window, collectedWarnings)
        fetched.push(result)
      }
      if (ticket !== inFlight) return
      series.value = fetched.filter((r): r is MetricSeriesDto => r !== null)
      warnings.value = collectedWarnings
      hasLoaded.value = true
    } catch (e) {
      if (ticket === inFlight) {
        error.value = e instanceof Error ? e.message : String(e)
      }
    } finally {
      if (ticket === inFlight) loading.value = false
    }
  }

  async function fetchSeries(
    binding: MetricBinding,
    window: TimeWindow,
    collectedWarnings: ResolutionWarning[]
  ): Promise<MetricSeriesDto | null> {
    const resolution: Resolution = catalog.resolve(binding)
    if (resolution.kind === 'no-match') return null
    if (resolution.kind === 'ambiguous') {
      collectedWarnings.push({
        serviceName: binding.serviceName ?? null,
        requestedId: resolution.requestedId,
        available: resolution.available
      })
      return null
    }
    return cache.getPoints({
      resourceHash: resolution.binding.resourceHash,
      scopeName: resolution.binding.scopeName,
      instrumentName: resolution.binding.instrumentName,
      kind: resolution.binding.kind
    }, window, { includeAttributes })
  }

  watch(metrics, load, { immediate: true, deep: true })
  watch(range, load)
  watch(liveTick, load)

  return { series, loading, error, warnings, hasLoaded, reload: load }
}
