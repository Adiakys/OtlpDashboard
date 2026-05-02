import { computed, type ComputedRef } from 'vue'
import { expandMetricBinding } from '~/lib/htmlEngine/parameterExpansion'
import type { MetricBinding } from '../types'

/**
 * Wrap a config that holds an optional `metric: MetricBinding | null` into the
 * `[binding]` array shape that `useWidgetSeries` expects. Returns an empty
 * array when no metric is configured — `useWidgetSeries` handles that as a
 * no-op fetch.
 *
 * When `parameters` is provided, every `${param}` placeholder inside the
 * binding's logical-key fields is substituted from that map before the
 * fetch. Lets a library widget ship with the metric path baked in
 * (e.g. `serviceName: "${service}"`) and have the user fill only the
 * parameter via the config form.
 */
export function useSingleMetric(
  metric: () => MetricBinding | null | undefined,
  parameters?: () => Record<string, string | number | boolean> | undefined
): ComputedRef<MetricBinding[]> {
  return computed(() => {
    const m = metric()
    if (!m) return []
    if (!parameters) return [m]
    const expanded = expandMetricBinding(m, parameters())
    return expanded ? [expanded] : []
  })
}
