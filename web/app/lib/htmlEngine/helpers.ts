import type { Helper } from './templateRenderer'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { pickThreshold, type ThresholdStop } from '~/lib/units/thresholds'

/**
 * Whitelist of helpers exposed to widget templates. Each helper is a
 * pure function (no `this`, no closures over outer state) and returns
 * a primitive — the renderer escapes the output before it hits the DOM,
 * so a helper that returned HTML would still be sanitised downstream.
 *
 * Adding a helper is intentional: each entry is a permission grant that
 * library authors can rely on. Don't add anything that touches `window`,
 * fetch, eval, dynamic imports, or the DOM.
 */
export const TEMPLATE_HELPERS: Record<string, Helper> = {
  /**
   * Format a numeric value with the unit-aware formatter shared with
   * `metric-stat`. Falls back to `String(value)` when `kind` is missing
   * or invalid.
   *
   *   {{ format value 'ms' 2 }}  → "12.34 ms"
   */
  format: (value, kind, decimals) => {
    const n = Number(value)
    if (!Number.isFinite(n)) return ''
    if (typeof kind !== 'string') return String(n)
    const dec = Number(decimals ?? 2)
    return formatValue(n, kind as UnitKind, { decimals: Number.isFinite(dec) ? dec : 2 })
  },

  /**
   * Map a value into [0, 100] given inclusive bounds.
   *
   *   {{ percent value 0 100 }}
   */
  percent: (value, min, max) => {
    const v = Number(value), lo = Number(min), hi = Number(max)
    if (!Number.isFinite(v) || !Number.isFinite(lo) || !Number.isFinite(hi) || hi === lo) return '0'
    const pct = ((v - lo) / (hi - lo)) * 100
    return Math.max(0, Math.min(100, pct)).toFixed(1)
  },

  /**
   * Pick the matching threshold and return its colour as a CSS-safe
   * value. Returns empty when no threshold matches (the template can
   * fall back to a default colour via CSS).
   *
   *   <div style='color: {{ thresholdColor value thresholds }}'>
   *
   * `thresholds` is expected to be the standard `ThresholdStop[]`
   * shape — an array of `{ value, color }` rows. Anything else returns
   * empty.
   */
  thresholdColor: (value, thresholds) => {
    const v = Number(value)
    if (!Number.isFinite(v) || !Array.isArray(thresholds)) return ''
    const stop = pickThreshold(v, thresholds as ThresholdStop[])
    return stop?.color ?? ''
  },

  /**
   * Class-name variant. Returns one of `vellum-th-ok`, `vellum-th-warn`,
   * `vellum-th-bad`, or empty — derived from the threshold *index*,
   * letting the template ship pre-styled CSS classes for the three
   * tones without the author needing to thread colour values.
   */
  thresholdClass: (value, thresholds) => {
    const v = Number(value)
    if (!Number.isFinite(v) || !Array.isArray(thresholds)) return ''
    const list = thresholds as ThresholdStop[]
    const stop = pickThreshold(v, list)
    if (!stop) return ''
    const index = list.indexOf(stop)
    if (index <= 0) return 'vellum-th-ok'
    if (index === list.length - 1) return 'vellum-th-bad'
    return 'vellum-th-warn'
  },

  /**
   * Relative time label. Accepts an ISO-8601 string or a number (ms or
   * unix-seconds — the helper auto-detects on magnitude).
   *
   *   {{ dateAgo lastSeen }}
   */
  dateAgo: (input) => {
    const t = parseTimestamp(input)
    if (t === null) return ''
    const diffMs = Date.now() - t
    if (diffMs < 0) return 'in the future'
    const sec = Math.floor(diffMs / 1000)
    if (sec < 60) return `${sec}s ago`
    const min = Math.floor(sec / 60)
    if (min < 60) return `${min}m ago`
    const hr = Math.floor(min / 60)
    if (hr < 24) return `${hr}h ago`
    const days = Math.floor(hr / 24)
    return `${days}d ago`
  },

  /** Plural picker — Italian/English friendly. */
  pluralize: (n, singular, plural) => {
    const count = Number(n)
    if (!Number.isFinite(count)) return ''
    return count === 1 ? String(singular ?? '') : String(plural ?? '')
  },

  // ---- comparators (for use inside {{#if ...}}) -----------------------
  eq:  (a, b) => a === b || String(a) === String(b),
  neq: (a, b) => !(a === b || String(a) === String(b)),
  lt:  (a, b) => Number(a) < Number(b),
  lte: (a, b) => Number(a) <= Number(b),
  gt:  (a, b) => Number(a) > Number(b),
  gte: (a, b) => Number(a) >= Number(b),

  /**
   * Class name keyed off the saturation of `value` against `max`. Returns
   * one of `vellum-th-ok` / `-warn` / `-bad` matching the same naming
   * convention `thresholdClass` produces, so templates can ship a single
   * stylesheet for both. `warnPct` and `badPct` default to 50 / 80.
   *
   *   class='liquid {{ loadClass backends.value max.value 50 80 }}'
   *
   * Decoupled from `thresholdClass` because the latter expects absolute
   * threshold stops; this one is the right tool when the threshold is
   * "fraction of capacity" (connections vs max_connections, free disk vs
   * total, etc.) where the numerator alone is meaningless.
   */
  loadClass: (value, max, warnPct, badPct) => {
    const v = Number(value)
    const m = Number(max)
    if (!Number.isFinite(v) || !Number.isFinite(m) || m <= 0) return 'vellum-th-ok'
    const pct = (v / m) * 100
    const warn = Number(warnPct ?? 50)
    const bad = Number(badPct ?? 80)
    if (pct >= bad) return 'vellum-th-bad'
    if (pct >= warn) return 'vellum-th-warn'
    return 'vellum-th-ok'
  },

  /**
   * Defaulting helper — returns the first defined/non-empty argument.
   *
   *   {{ default value '—' }}
   */
  default: (...values) => {
    for (const v of values) {
      if (v !== null && v !== undefined && v !== '') return v
    }
    return ''
  }
}

function parseTimestamp(input: unknown): number | null {
  if (typeof input === 'number') {
    // Heuristic: < 1e12 looks like seconds, ≥ 1e12 looks like ms.
    if (!Number.isFinite(input)) return null
    return input < 1e12 ? input * 1000 : input
  }
  if (typeof input === 'string') {
    const t = Date.parse(input)
    return Number.isFinite(t) ? t : null
  }
  return null
}
