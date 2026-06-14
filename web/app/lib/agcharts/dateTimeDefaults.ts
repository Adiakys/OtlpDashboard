import type { AgChartOptions } from 'ag-charts-community'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

/**
 * Make every chart's time render through {@link dateTimeFormat} (honoring the
 * OS 12h/24h preference) without each widget wiring it up — applied at the
 * single point every chart passes through (`AppChart`).
 *
 * Two gaps get filled on charts that have a `type: 'time'` axis:
 *  - the axis itself gets a `label.formatter` (tick labels);
 *  - a series with no `tooltip.renderer` gets one. AG Charts' built-in tooltip
 *    formats its time heading from the locale's own hour cycle (AM/PM on a 24h
 *    system); we replace it with a renderer that mirrors AG's default shape
 *    (`{ heading, data: [{ label, value }] }`, so the chrome is unchanged) but
 *    formats the heading via the helper. Returning the object form — not a raw
 *    string — keeps the default tooltip styling.
 *
 * Axes/series that already set a `format`/`formatter`/`renderer` are left
 * untouched, so widgets keep their own richer tooltips.
 */
export function applyDateTimeDefaults(options: AgChartOptions, locale?: string): AgChartOptions {
  const opts = options as { axes?: TimeAxisLike[]; series?: CartesianSeriesLike[] }
  const axes = opts.axes
  // Only cartesian charts have axes; bail out unless one is a time axis so we
  // never touch pie/gauge/etc.
  if (!Array.isArray(axes) || !axes.some(a => a?.type === 'time')) return options

  const series = Array.isArray(opts.series) ? opts.series : []
  return {
    ...options,
    axes: axes.map(a => withTimeAxisFormatter(a, locale)),
    series: series.map(s => withTimeTooltip(s, locale))
  } as AgChartOptions
}

interface AxisLabelLike {
  format?: string
  formatter?: unknown
  [k: string]: unknown
}
interface TimeAxisLike {
  type?: string
  label?: AxisLabelLike
  [k: string]: unknown
}

function withTimeAxisFormatter(axis: TimeAxisLike, locale?: string): TimeAxisLike {
  if (axis?.type !== 'time') return axis
  const label = axis.label
  if (label?.format || label?.formatter) return axis
  return {
    ...axis,
    label: {
      ...(label ?? {}),
      formatter: ({ value }: { value: unknown }) => dateTimeFormat(value as Date, 'time-seconds', locale)
    }
  }
}

interface CartesianSeriesLike {
  xKey?: string
  yKey?: string
  tooltip?: { renderer?: unknown; [k: string]: unknown }
  [k: string]: unknown
}

interface DefaultTooltipParams {
  datum: Record<string, unknown>
  yValue?: unknown
  yName?: string
}

function withTimeTooltip(s: CartesianSeriesLike, locale?: string): CartesianSeriesLike {
  if (!s || s.tooltip?.renderer || !s.xKey) return s
  const xKey = s.xKey
  const yKey = s.yKey
  return {
    ...s,
    tooltip: {
      ...(s.tooltip ?? {}),
      // Mirror AG Charts' default tooltip shape so the chrome is identical;
      // only the time heading changes (helper-formatted, OS hour cycle).
      // A non-null `label` keeps AG out of its "compact" tooltip mode (the
      // narrow, single-line variant that drops the value); we fall back to the
      // yKey when the series has no yName.
      renderer: (params: DefaultTooltipParams) => ({
        heading: dateTimeFormat(params.datum?.[xKey] as Date, 'time-seconds', locale),
        data: [{ label: params.yName ?? yKey ?? '', value: formatValue(pickValue(params, xKey, yKey), locale) }]
      })
    }
  }
}

/** Resolve the y value: explicit `yValue`, then `datum[yKey]`, then the first
 *  finite number on the datum that isn't the x key (covers series whose value
 *  lives under a unit-suffixed key the renderer can't know upfront). */
function pickValue(params: DefaultTooltipParams, xKey: string, yKey?: string): unknown {
  if (typeof params.yValue === 'number') return params.yValue
  if (yKey != null && params.datum[yKey] != null) return params.datum[yKey]
  for (const k of Object.keys(params.datum)) {
    if (k === xKey) continue
    const v = params.datum[k]
    if (typeof v === 'number' && Number.isFinite(v)) return v
  }
  return undefined
}

function formatValue(value: unknown, locale?: string): string {
  if (typeof value === 'number' && Number.isFinite(value)) return value.toLocaleString(locale)
  return value == null ? '' : String(value)
}
