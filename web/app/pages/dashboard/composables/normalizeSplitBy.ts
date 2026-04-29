import type { SplitBy } from '~/lib/agcharts/seriesGrouping'

/**
 * Map a widget config's `splitBy` field (string | null) to the `SplitBy`
 * shape expected by `groupPoints`. Empty/null falls back to "all attributes"
 * so the widget renders something useful out of the box; a non-empty value
 * narrows the grouping to a single attribute key.
 */
export function normalizeSplitBy(raw: string | null | undefined): SplitBy {
  if (!raw) return 'all'
  return [raw]
}
