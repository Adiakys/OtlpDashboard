import { computed, type ComputedRef, type Ref } from 'vue'
import type { MetricSeriesDto } from '~/services/types'
import { reduce, type CalcMode } from '~/lib/units/calc'

/**
 * Collapse the first loaded series to a single scalar via the configured
 * reduction (`last`/`mean`/…). Stat, Gauge, and Bar gauge widgets all share
 * this shape — we centralize it so a tweak to point selection (e.g. drop
 * non-finite values) only happens once.
 */
export function useReducedScalar(
  series: Ref<MetricSeriesDto[]>,
  calc: ComputedRef<CalcMode>
): ComputedRef<number | null> {
  return computed(() => {
    const points = series.value[0]?.points ?? []
    return reduce(points.map(p => Number(p.value)), calc.value)
  })
}
