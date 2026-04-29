/**
 * Threshold matching for value-based coloring. A `ThresholdStop` is
 * `{ value, color }`; a list of stops is interpreted as a step function: pick
 * the highest stop whose `value` is ≤ the input. The "base" color is the stop
 * with `value = -Infinity` — by convention the first stop in the list — and
 * matches every value below the next stop.
 *
 * Colors are stored as hex strings so they survive JSON round-trips and don't
 * depend on the active theme. Widgets that want a theme-aware default should
 * keep the threshold list empty and fall back to their own theme-aware color.
 */

export interface ThresholdStop {
  /** Boundary in the metric's base unit. */
  value: number
  /** Hex color (`#rrggbb`) or any valid CSS color string. */
  color: string
}

/**
 * Pick the matching threshold for `value`. Returns `null` if `thresholds` is
 * empty or `value` is not finite.
 *
 * The list does not need to be pre-sorted — we sort a shallow copy by value.
 */
export function pickThreshold(value: number, thresholds: ThresholdStop[]): ThresholdStop | null {
  if (!Number.isFinite(value) || thresholds.length === 0) return null
  const sorted = [...thresholds].sort((a, b) => a.value - b.value)
  let match: ThresholdStop | null = null
  for (const t of sorted) {
    if (value >= t.value) match = t
    else break
  }
  // Fallback when `value` is below the lowest stop: treat the lowest as base.
  return match ?? sorted[0] ?? null
}
