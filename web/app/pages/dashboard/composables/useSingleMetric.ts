import { computed, type ComputedRef } from 'vue'
import type { MetricBinding } from '../types'

/**
 * Wrap a config that holds an optional `metric: MetricBinding | null` into the
 * `[binding]` array shape that `useWidgetSeries` expects. Returns an empty
 * array when no metric is configured — `useWidgetSeries` handles that as a
 * no-op fetch.
 */
export function useSingleMetric(
  metric: () => MetricBinding | null | undefined
): ComputedRef<MetricBinding[]> {
  return computed(() => {
    const m = metric()
    return m ? [m] : []
  })
}
