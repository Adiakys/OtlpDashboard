import type { TimeWindow } from '~/services/types'

/**
 * Time-range preset table shared by the date-time picker and the page
 * composables. Keys are URL-stable (`?range=1h`) — never rename without
 * a redirect.
 *
 * The picker treats these as "rolling" anchors at `now`. Composables
 * use {@link presetToWindow} to recompute the absolute window on
 * hydration and (in live mode) on each tick, so the trailing edge
 * doesn't drift after navigation away and back.
 */
export const TIME_RANGE_PRESET_MINUTES: Record<string, number> = {
  '5m': 5,
  '15m': 15,
  '1h': 60,
  '6h': 60 * 6,
  '24h': 60 * 24,
  '7d': 60 * 24 * 7
}

export type TimeRangePresetKey = keyof typeof TIME_RANGE_PRESET_MINUTES

export function isKnownPresetKey(value: unknown): value is TimeRangePresetKey {
  return typeof value === 'string' && value in TIME_RANGE_PRESET_MINUTES
}

export function presetToWindow(preset: TimeRangePresetKey, now: number = Date.now()): TimeWindow {
  // Non-null assertion is justified by the `TimeRangePresetKey` type — every
  // declared key has an entry in the table. `noUncheckedIndexedAccess` makes
  // TS flag the lookup as possibly-undefined anyway.
  const minutes = TIME_RANGE_PRESET_MINUTES[preset]!
  const to = new Date(now)
  const from = new Date(now - minutes * 60_000)
  return { from: from.toISOString(), to: to.toISOString() }
}
