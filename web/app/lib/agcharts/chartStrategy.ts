import type {
  AgChartOptions,
  AgCartesianAxisOptions,
  AgCartesianChartOptions,
  AgCartesianSeriesOptions
} from 'ag-charts-community'
import type { InstrumentDto, MetricSeriesDto } from '~/services/types'
import { describeGroup, groupPoints, type SplitBy, type SeriesGroup } from './seriesGrouping'
import { instrumentKey } from '~/pages/metrics/buildTree'

export type ChartType = 'line' | 'area' | 'column' | 'unsupported'

/**
 * Pick the visualization that best fits the metric semantics.
 *  - `Gauge` → line (instantaneous values).
 *  - `Sum` monotonic + Cumulative → area (running total).
 *  - `Sum` monotonic + Delta → column (per-interval increments).
 *  - `Sum` non-monotonic (UpDownCounter) → line.
 *  - `Histogram` / `ExponentialHistogram` → line over the per-point mean
 *    (sum/count); count and sum are kept as `_count`/`_sum` attributes.
 *  - `Summary` → line over each quantile's value; split-by `quantile` draws
 *    one line per percentile.
 *  - Anything else / Unspecified → line as a safe default.
 */
export function pickChartType(
  kind: string,
  temporality: string,
  isMonotonic: boolean
): ChartType {
  if (kind === 'Gauge') return 'line'
  if (kind === 'Sum') {
    if (!isMonotonic) return 'line'
    return temporality === 'Delta' ? 'column' : 'area'
  }
  if (kind === 'Histogram' || kind === 'ExponentialHistogram') {
    return temporality === 'Delta' ? 'column' : 'line'
  }
  if (kind === 'Summary') return 'line'
  return 'line'
}

interface BuildOptionsInput {
  /** Loaded series for the user's selection, in selection order. */
  series: MetricSeriesDto[]
  chartType: ChartType
  splitBy: SplitBy
  locale: string
  isDark: boolean
}

interface ChartDatum {
  time: number
  /**
   * Value lives under a unit-specific key (`value`, `value_ms`, …) so AG
   * Charts can route the series to the matching numeric axis. The key is
   * picked by `unitToYKey()` and stays consistent across every datum of a
   * given series.
   */
  [yKey: string]: number | undefined
  /** Per-point metadata captured from `_*` attributes (count/sum/min/max).
   *  Pulled into every datum so AG Charts tooltips can read them directly. */
  count?: number
  sum?: number
  min?: number
  max?: number
}

/**
 * Build AG Charts options for the selection. Each instrument contributes one
 * series per attribute group (per `splitBy`). The X axis is time; on the Y
 * axis we use AG Charts' native multi-axis support: one numeric axis per
 * distinct unit, identified by a unit-derived `yKey`. Series with no unit
 * land on a default axis. Each unit axis is titled `[unit]` so the user can
 * tell which scale belongs to which series at a glance.
 */
export function buildChartOptions(input: BuildOptionsInput): AgChartOptions {
  const { series, chartType, splitBy, locale, isDark } = input

  if (chartType === 'unsupported' || series.length === 0) {
    return emptyOptions(isDark)
  }

  const seriesType = chartType === 'column' ? 'bar' : chartType
  const allSeries: AgCartesianSeriesOptions[] = []
  // Preserve insertion order: first selected unit goes left, the rest right.
  const usedUnits = new Map<string, { unit: string | null; yKey: string }>()

  for (const s of series) {
    const unit = normalizeUnit(s.instrument.unit)
    const yKey = unitToYKey(unit)
    if (!usedUnits.has(yKey)) usedUnits.set(yKey, { unit, yKey })

    const groups = groupPoints(s.points, splitBy)
    const prefix = series.length > 1 ? `${s.instrument.name} ` : ''
    for (const g of groups) {
      const data = toData(g, yKey)
      if (data.length === 0) continue
      const name = prefix
        ? `${prefix}${describeGroup(g.attrs)}`
        : describeGroup(g.attrs)
      allSeries.push(buildSeries(seriesType, name, data, yKey))
    }
  }

  const fmt = new Intl.DateTimeFormat(locale, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })

  const options: AgCartesianChartOptions = {
    theme: isDark ? 'ag-default-dark' : 'ag-default',
    series: allSeries,
    axes: [
      {
        type: 'time',
        position: 'bottom',
        label: { format: '%H:%M:%S', formatter: ({ value }) => fmt.format(value as Date) },
        nice: true
      },
      ...buildYAxes([...usedUnits.values()])
    ],
    legend: {
      enabled: allSeries.length > 1,
      position: 'bottom'
    },
    background: { visible: false }
  }
  return options
}

/** Shared unit across instruments, or `null` if they disagree. */
export function sharedUnit(instruments: InstrumentDto[]): string | null {
  if (instruments.length === 0) return null
  const first = instruments[0]?.unit ?? null
  for (const i of instruments) {
    if ((i.unit ?? null) !== first) return null
  }
  return first
}

/** Distinct units used by the selection, in selection order. Empty/null
 *  units are reported as `null`. Used by the chart header to show the user
 *  which scales the chart is drawing. */
export function distinctUnits(instruments: InstrumentDto[]): (string | null)[] {
  const seen = new Set<string>()
  const out: (string | null)[] = []
  for (const i of instruments) {
    const u = normalizeUnit(i.unit)
    const key = u ?? '__none__'
    if (seen.has(key)) continue
    seen.add(key)
    out.push(u)
  }
  return out
}

/** Convenience: lookup loaded series for a selected instrument by composite key. */
export function pickSelectedSeries(
  selectedKeys: ReadonlySet<string>,
  loaded: ReadonlyMap<string, MetricSeriesDto>
): MetricSeriesDto[] {
  const out: MetricSeriesDto[] = []
  for (const key of selectedKeys) {
    const s = loaded.get(key)
    if (s) out.push(s)
  }
  return out
}

export { instrumentKey }
export type { SeriesGroup }

function emptyOptions(isDark: boolean): AgChartOptions {
  return {
    theme: isDark ? 'ag-default-dark' : 'ag-default',
    data: [],
    series: [],
    background: { visible: false }
  }
}

function buildSeries(
  type: 'line' | 'area' | 'bar',
  name: string,
  data: ChartDatum[],
  yKey: string
): AgCartesianSeriesOptions {
  const tooltip = { renderer: tooltipRenderer }
  if (type === 'line') {
    return { type: 'line', xKey: 'time', yKey, yName: name, data, marker: { enabled: false }, tooltip }
  }
  if (type === 'area') {
    return { type: 'area', xKey: 'time', yKey, yName: name, data, fillOpacity: 0.25, tooltip }
  }
  return { type: 'bar', xKey: 'time', yKey, yName: name, data, direction: 'vertical', tooltip }
}

function buildYAxes(
  units: { unit: string | null; yKey: string }[]
): AgCartesianAxisOptions[] {
  // Stack every numeric axis on the left. AG Charts handles the layout of
  // multiple same-position axes natively, so no extra wiring is needed.
  return units.map(u => ({
    type: 'number',
    position: 'left',
    keys: [u.yKey],
    label: { formatter: ({ value }) => formatNumber(value as number) }
  }))
}

function toData(group: SeriesGroup, yKey: string): ChartDatum[] {
  const out: ChartDatum[] = []
  for (const p of group.points) {
    const time = new Date(p.time).getTime()
    if (!Number.isFinite(time) || !Number.isFinite(p.value)) continue
    const datum: ChartDatum = { time, [yKey]: p.value }
    const count = readNumber(p.attributes['_count'])
    const sum = readNumber(p.attributes['_sum'])
    const min = readNumber(p.attributes['_min'])
    const max = readNumber(p.attributes['_max'])
    if (count !== undefined) datum.count = count
    if (sum !== undefined) datum.sum = sum
    if (min !== undefined) datum.min = min
    if (max !== undefined) datum.max = max
    out.push(datum)
  }
  return out
}

function readNumber(v: unknown): number | undefined {
  if (typeof v === 'number' && Number.isFinite(v)) return v
  if (typeof v === 'bigint') return Number(v)
  if (typeof v === 'string') {
    const n = Number(v)
    return Number.isFinite(n) ? n : undefined
  }
  return undefined
}

function normalizeUnit(unit: string | null | undefined): string | null {
  if (unit === null || unit === undefined) return null
  const trimmed = unit.trim()
  // OTel "1" means "dimensionless" — render as no unit, no axis title.
  if (trimmed === '' || trimmed === '1') return null
  return trimmed
}

/** Stable, JS-identifier-safe `yKey` derived from the unit. Series with the
 *  same unit share the same key (and thus the same Y axis); series with no
 *  unit collide on the default `'value'` key. */
function unitToYKey(unit: string | null): string {
  if (unit === null) return 'value'
  const slug = unit.replace(/[^A-Za-z0-9]+/g, '_').toLowerCase().replace(/^_+|_+$/g, '')
  return slug.length === 0 ? 'value' : `value_${slug}`
}

interface TooltipParams {
  datum: ChartDatum
  yName?: string
  xKey: string
  yKey: string
  xValue?: unknown
  yValue?: unknown
  color?: string
}

function tooltipRenderer(params: TooltipParams): { title?: string; content: string } {
  const { datum, yName, yValue } = params
  const value = typeof yValue === 'number' ? yValue : (datum[params.yKey] ?? 0)
  const time = new Date(datum.time)
  const timeLabel = time.toLocaleTimeString([], {
    hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3
  } as Intl.DateTimeFormatOptions)
  const lines: string[] = [`<b>${escapeHtml(formatNumber(value as number))}</b> at ${escapeHtml(timeLabel)}`]
  if (datum.count !== undefined) lines.push(`count: ${formatNumber(datum.count)}`)
  if (datum.sum !== undefined) lines.push(`sum: ${formatNumber(datum.sum)}`)
  if (datum.min !== undefined) lines.push(`min: ${formatNumber(datum.min)}`)
  if (datum.max !== undefined) lines.push(`max: ${formatNumber(datum.max)}`)
  return {
    title: yName,
    content: lines.join('<br/>')
  }
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c] as string))
}

function formatNumber(value: number): string {
  if (!Number.isFinite(value)) return ''
  if (Math.abs(value) >= 1000) return value.toLocaleString()
  if (Number.isInteger(value)) return value.toString()
  return value.toFixed(2).replace(/\.?0+$/, '')
}
