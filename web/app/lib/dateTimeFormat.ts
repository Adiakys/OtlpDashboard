/**
 * Single entry point for every date/time string the UI renders.
 *
 * Why this lives in one place: previously every component built its own
 * `Intl.DateTimeFormat` and inherited the i18n locale's hour cycle, so
 * en-US users always saw AM/PM even when their OS was set to 24h. This
 * module overrides `hour12` based on the *system* preference (resolved
 * once via the user agent's default locale) so the active i18n locale
 * still drives month names, date order and separators, but the clock
 * cycle matches the OS.
 */

export type DateTimePreset =
  /** Short date + short time — e.g. `5/30/26, 12:34`. Toolbar subtitles, picker summary. */
  | 'datetime'
  /** Short date + time with seconds — e.g. `5/30/26, 12:34:56`. Live windows. */
  | 'datetime-seconds'
  /** Medium date + medium time — e.g. `May 30, 2026, 12:34:56`. Detail panels. */
  | 'datetime-long'
  /** `12:34` — heatmap buckets. */
  | 'time'
  /** `12:34:56` — chart axes, stream rows, histogram tooltips. */
  | 'time-seconds'
  /** `12:34:56.123` — high-precision tooltips (spans, metric points). */
  | 'time-ms'

const PRESET_OPTIONS: Record<DateTimePreset, Intl.DateTimeFormatOptions> = {
  'datetime':         { dateStyle: 'short',  timeStyle: 'short'  },
  'datetime-seconds': { dateStyle: 'short',  timeStyle: 'medium' },
  'datetime-long':    { dateStyle: 'medium', timeStyle: 'medium' },
  'time':             { hour: '2-digit', minute: '2-digit' },
  'time-seconds':     { hour: '2-digit', minute: '2-digit', second: '2-digit' },
  'time-ms':          { hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 }
}

// `resolvedOptions().hour12` on the user agent's default locale reflects
// that locale's standard hour cycle, which browsers derive from OS region
// settings on Windows/macOS/Linux. Cached because the OS preference can't
// change without a page reload.
let cachedSystemHour12: boolean | null = null
function systemHour12(): boolean {
  if (cachedSystemHour12 === null) {
    cachedSystemHour12 = new Intl.DateTimeFormat(undefined, { hour: 'numeric' })
      .resolvedOptions().hour12 ?? false
  }
  return cachedSystemHour12
}

const formatterCache = new Map<string, Intl.DateTimeFormat>()
function getFormatter(locale: string | undefined, preset: DateTimePreset): Intl.DateTimeFormat {
  const key = `${locale ?? ''}|${preset}`
  let fmt = formatterCache.get(key)
  if (!fmt) {
    fmt = new Intl.DateTimeFormat(locale, {
      ...PRESET_OPTIONS[preset],
      hour12: systemHour12()
    })
    formatterCache.set(key, fmt)
  }
  return fmt
}

/**
 * Format a date/time value using a named preset, honoring the OS 12h/24h
 * preference. `locale` controls everything else (month names, ordering,
 * separators); when omitted, the formatter falls back to the user agent
 * default.
 */
export function dateTimeFormat(
  value: Date | number | string,
  preset: DateTimePreset = 'datetime',
  locale?: string
): string {
  const d = value instanceof Date ? value : new Date(value)
  return getFormatter(locale, preset).format(d)
}
