/**
 * Unit-aware formatting for widget values. The "unit kind" determines the
 * auto-scaling strategy (e.g. bytes → B/KiB/MiB/GiB), the formatter then
 * writes the numeric part with the active locale and the requested decimals.
 *
 * `formatValue` is intended for everything end-user-facing: stat labels, axis
 * tick labels, tooltips. `parseUnitInput` is the inverse — used by the
 * threshold editor so the user can type "100 MB" and we store the byte value.
 */

export type UnitKind =
  | 'none'
  | 'short'
  | 'bytes'
  | 'bps'
  | 'seconds'
  | 'ms'
  | 'percent'
  | 'percent-unit'
  | 'ops'

export const UNIT_KINDS: UnitKind[] = [
  'none',
  'short',
  'bytes',
  'bps',
  'seconds',
  'ms',
  'percent',
  'percent-unit',
  'ops'
]

export interface FormatOptions {
  decimals?: number
  locale?: string
}

interface ScaledValue {
  value: number
  suffix: string
}

const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB']
const BPS_UNITS = ['bit/s', 'Kibit/s', 'Mibit/s', 'Gibit/s', 'Tibit/s']
const SHORT_UNITS = ['', 'K', 'M', 'G', 'T', 'P']

/** Format `value` according to `unit`. Returns just the value+suffix string. */
export function formatValue(value: number, unit: UnitKind, options: FormatOptions = {}): string {
  if (!Number.isFinite(value)) return '—'
  const decimals = options.decimals ?? 2
  const locale = options.locale ?? undefined

  switch (unit) {
    case 'none':
      return formatNumber(value, decimals, locale)
    case 'short': {
      const scaled = scalePow1000(value, SHORT_UNITS)
      return `${formatNumber(scaled.value, decimals, locale)}${scaled.suffix}`
    }
    case 'bytes': {
      const scaled = scalePow1024(value, BYTE_UNITS)
      return `${formatNumber(scaled.value, decimals, locale)} ${scaled.suffix}`
    }
    case 'bps': {
      const scaled = scalePow1024(value, BPS_UNITS)
      return `${formatNumber(scaled.value, decimals, locale)} ${scaled.suffix}`
    }
    case 'seconds':
      return formatDuration(value, decimals, locale)
    case 'ms':
      return formatDuration(value / 1000, decimals, locale)
    case 'percent':
      return `${formatNumber(value, decimals, locale)}%`
    case 'percent-unit':
      return `${formatNumber(value * 100, decimals, locale)}%`
    case 'ops':
      return `${formatNumber(value, decimals, locale)} ops`
  }
}

/**
 * Parse a user-typed threshold like "100 MB" or "200ms" into the value
 * expressed in the unit kind's base. Returns NaN when the input cannot be
 * parsed — callers should treat that as a validation error.
 *
 *  - bytes:   "100" → 100 ; "100 KB"/"100KiB" → 102400 ; "1MB" → 1048576
 *  - bps:     same family with bit/s suffix
 *  - seconds: "200ms" → 0.2 ; "1.5" → 1.5 ; "1m" → 60
 *  - ms:      "200" → 200 ; "1.5s" → 1500
 *  - percent / percent-unit: "50" → 50 / "0.5" → 0.5 (no suffix parsing)
 *  - none / short / ops: bare number
 */
export function parseUnitInput(text: string, unit: UnitKind): number {
  const trimmed = text.trim()
  if (trimmed.length === 0) return NaN

  switch (unit) {
    case 'bytes':
      return parseByteLike(trimmed, BYTE_UNITS, 1024)
    case 'bps':
      return parseByteLike(trimmed, BPS_UNITS, 1024)
    case 'seconds':
      return parseDuration(trimmed, 1)
    case 'ms':
      return parseDuration(trimmed, 1000)
    default: {
      const n = Number(trimmed)
      return Number.isFinite(n) ? n : NaN
    }
  }
}

function formatNumber(value: number, decimals: number, locale: string | undefined): string {
  return new Intl.NumberFormat(locale, {
    maximumFractionDigits: decimals,
    minimumFractionDigits: 0
  }).format(value)
}

function scalePow1000(value: number, suffixes: string[]): ScaledValue {
  if (value === 0) return { value: 0, suffix: suffixes[0]! }
  const sign = Math.sign(value)
  const abs = Math.abs(value)
  let i = 0
  let v = abs
  while (v >= 1000 && i < suffixes.length - 1) {
    v /= 1000
    i++
  }
  return { value: sign * v, suffix: suffixes[i]! }
}

function scalePow1024(value: number, suffixes: string[]): ScaledValue {
  if (value === 0) return { value: 0, suffix: suffixes[0]! }
  const sign = Math.sign(value)
  const abs = Math.abs(value)
  let i = 0
  let v = abs
  while (v >= 1024 && i < suffixes.length - 1) {
    v /= 1024
    i++
  }
  return { value: sign * v, suffix: suffixes[i]! }
}

function formatDuration(seconds: number, decimals: number, locale: string | undefined): string {
  const abs = Math.abs(seconds)
  if (abs === 0) return `0 s`
  if (abs < 1e-6) return `${formatNumber(seconds * 1e9, decimals, locale)} ns`
  if (abs < 1e-3) return `${formatNumber(seconds * 1e6, decimals, locale)} µs`
  if (abs < 1) return `${formatNumber(seconds * 1e3, decimals, locale)} ms`
  if (abs < 60) return `${formatNumber(seconds, decimals, locale)} s`
  if (abs < 3600) return `${formatNumber(seconds / 60, decimals, locale)} min`
  if (abs < 86400) return `${formatNumber(seconds / 3600, decimals, locale)} h`
  return `${formatNumber(seconds / 86400, decimals, locale)} d`
}

function parseByteLike(input: string, suffixes: string[], base: number): number {
  // Match "<number> [unit]" with optional whitespace; unit is suffix-or-prefix
  // free-form, we fall back to a case-insensitive comparison against the
  // suffix table.
  const match = /^(-?\d+(?:[.,]\d+)?)\s*([a-zA-Z\/]+)?$/.exec(input)
  if (!match) return NaN
  const num = Number(match[1]!.replace(',', '.'))
  if (!Number.isFinite(num)) return NaN
  const rawSuffix = (match[2] ?? '').toLowerCase()
  if (rawSuffix.length === 0) return num
  // Normalize common variants: "kb" → "kib", "mb" → "mib", etc. Grafana does
  // the same — most users mean IEC even when they write SI suffixes.
  const normalized = normalizeBinarySuffix(rawSuffix)
  const idx = suffixes.findIndex(s => s.toLowerCase() === normalized)
  if (idx === -1) return NaN
  return num * Math.pow(base, idx)
}

function normalizeBinarySuffix(s: string): string {
  // Map "b" → "b" (or "bit/s" for bps), "kb"/"k" → "kib"/"kibit/s", etc.
  const stripped = s.replace(/\s+/g, '').toLowerCase()
  const map: Record<string, string> = {
    'b': 'b',
    'kb': 'kib',
    'mb': 'mib',
    'gb': 'gib',
    'tb': 'tib',
    'pb': 'pib',
    'k': 'kib',
    'm': 'mib',
    'g': 'gib',
    't': 'tib',
    'p': 'pib',
    'bit/s': 'bit/s',
    'kbit/s': 'kibit/s',
    'mbit/s': 'mibit/s',
    'gbit/s': 'gibit/s',
    'tbit/s': 'tibit/s'
  }
  return map[stripped] ?? stripped
}

function parseDuration(input: string, perSecond: number): number {
  // Accepts: bare number ("100" → 100 in unit base), or with suffix
  // ns/us/µs/ms/s/min/h/d. `perSecond` is the multiplier from seconds → unit
  // base (1 for seconds-typed values, 1000 for ms-typed).
  const match = /^(-?\d+(?:[.,]\d+)?)\s*([a-zµ]+)?$/i.exec(input)
  if (!match) return NaN
  const num = Number(match[1]!.replace(',', '.'))
  if (!Number.isFinite(num)) return NaN
  const suffix = (match[2] ?? '').toLowerCase()
  if (suffix.length === 0) return num
  const seconds: number = ({
    'ns': num * 1e-9,
    'us': num * 1e-6,
    'µs': num * 1e-6,
    'ms': num * 1e-3,
    's': num,
    'sec': num,
    'min': num * 60,
    'm': num * 60,
    'h': num * 3600,
    'd': num * 86400
  } as Record<string, number>)[suffix] ?? NaN
  if (!Number.isFinite(seconds)) return NaN
  return seconds * perSecond
}
