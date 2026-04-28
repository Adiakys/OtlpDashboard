import type { MetricPointDto, MetricSeriesDto } from '~/services/types'

/**
 * `'all'` — one group per unique combination of attributes (default).
 * `string[]` — one group per distinct combination of the listed keys; an empty
 *   array means "ignore attributes" (single aggregated group per metric).
 */
export type SplitBy = 'all' | string[]

export interface SeriesGroup {
  /** Stable label combining attribute key/values; `'(no attrs)'` if empty. */
  key: string
  /** Attribute subset that defines this group (keys honoured by `splitBy`). */
  attrs: Record<string, unknown>
  /** Points sorted by time ASC — required by AG Charts time axis. */
  points: MetricPointDto[]
}

const NO_ATTRS_KEY = '(no attrs)'

/**
 * Group `points` into series according to `splitBy`. Each group's points are
 * returned sorted by time ascending — AG Charts requires monotonic x for time
 * axes and otherwise renders zig-zag artifacts.
 *
 * Attribute keys starting with `_` are treated as per-point metadata produced
 * by the translator (e.g. `_count`, `_sum`, `_min`, `_max` on Histogram
 * points). They are intentionally excluded from grouping — they change at
 * every point and would otherwise create one series per point — but stay on
 * the original points for tooltips and the raw-points table.
 */
export function groupPoints(points: MetricPointDto[], splitBy: SplitBy): SeriesGroup[] {
  const groups = new Map<string, SeriesGroup>()

  for (const p of points) {
    const attrs = pickAttributes(p.attributes, splitBy)
    const key = serializeKey(attrs)
    let g = groups.get(key)
    if (!g) {
      g = { key, attrs, points: [] }
      groups.set(key, g)
    }
    g.points.push(p)
  }

  for (const g of groups.values()) {
    g.points.sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())
  }

  return [...groups.values()].sort((a, b) => a.key.localeCompare(b.key))
}

/** Union of attribute keys present across loaded series, excluding the
 *  underscore-prefixed metadata so the Split-by dropdown only offers keys
 *  whose values discriminate one series from another. */
export function availableAttributeKeys(series: Iterable<MetricSeriesDto>): string[] {
  const keys = new Set<string>()
  for (const s of series) {
    for (const p of s.points) {
      for (const k of Object.keys(p.attributes)) {
        if (!isMetadataKey(k)) keys.add(k)
      }
    }
  }
  return [...keys].sort()
}

/** True for keys produced by the translator as per-point metadata, e.g.
 *  `_count`, `_sum`, `_min`, `_max`. */
export function isMetadataKey(key: string): boolean {
  return key.length > 0 && key.startsWith('_')
}

/** Human-readable label for a group, e.g. `{method=GET, status=200}`. */
export function describeGroup(attrs: Record<string, unknown>): string {
  const entries = Object.entries(attrs)
  if (entries.length === 0) return NO_ATTRS_KEY
  return '{' + entries.map(([k, v]) => `${k}=${formatValue(v)}`).join(', ') + '}'
}

function pickAttributes(
  attrs: Record<string, unknown>,
  splitBy: SplitBy
): Record<string, unknown> {
  if (splitBy === 'all') {
    const out: Record<string, unknown> = {}
    for (const [k, v] of Object.entries(attrs)) {
      if (!isMetadataKey(k)) out[k] = v
    }
    return sortByKey(out)
  }
  if (splitBy.length === 0) return {}
  const out: Record<string, unknown> = {}
  for (const k of splitBy) {
    if (k in attrs) out[k] = attrs[k]
  }
  return sortByKey(out)
}

function sortByKey(o: Record<string, unknown>): Record<string, unknown> {
  const sorted: Record<string, unknown> = {}
  for (const k of Object.keys(o).sort()) sorted[k] = o[k]
  return sorted
}

function serializeKey(attrs: Record<string, unknown>): string {
  const entries = Object.entries(attrs)
  if (entries.length === 0) return NO_ATTRS_KEY
  return entries.map(([k, v]) => `${k}=${formatValue(v)}`).join('|')
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean') return String(v)
  return JSON.stringify(v)
}
