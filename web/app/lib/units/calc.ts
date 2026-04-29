/**
 * Stat-style reductions over a series of points. Applied by widgets that
 * collapse a time-series to a single number (Stat, Gauge, Bar gauge).
 */

export type CalcMode = 'last' | 'mean' | 'min' | 'max' | 'sum'

export const CALC_MODES: CalcMode[] = ['last', 'mean', 'min', 'max', 'sum']

/** Reduce `values` to a scalar using the given mode. Returns `null` if there
 *  are no finite values to operate on. */
export function reduce(values: readonly number[], mode: CalcMode): number | null {
  const finite: number[] = []
  for (const v of values) if (Number.isFinite(v)) finite.push(v)
  if (finite.length === 0) return null

  switch (mode) {
    case 'last':
      return finite[finite.length - 1]!
    case 'mean':
      return finite.reduce((a, b) => a + b, 0) / finite.length
    case 'min':
      return Math.min(...finite)
    case 'max':
      return Math.max(...finite)
    case 'sum':
      return finite.reduce((a, b) => a + b, 0)
  }
}
