import type { ChartType } from '~/lib/agcharts/chartStrategy'
import type { CalcMode } from '~/lib/units/calc'
import type { ThresholdStop } from '~/lib/units/thresholds'
import type { UnitKind } from '~/lib/units/format'
import type { InstrumentDto } from '~/services/types'

export type { CalcMode, ThresholdStop, UnitKind }

/** Widget kinds shipped in v1. New kinds extend the union and the registry. */
export type WidgetKind =
  | 'metric-stat'
  | 'metric-line'
  | 'metric-sparkline'
  | 'metric-gauge'
  | 'metric-bar-gauge'
  | 'metric-pie'
  | 'metric-heatmap'
  | 'recent-traces'
  | 'logs-stream'
  | 'text'

/**
 * Server-side instrument identity (the four fields the metrics API uses as a
 * lookup key). Stored verbatim in widget configs so the widget renders
 * deterministically across reloads.
 */
export interface MetricBinding {
  resourceHash: string
  scopeName: string
  instrumentName: string
  kind: string
  /** Display-only fallback so the SPA doesn't show a blank label while the instrument list is loading. */
  serviceName?: string | null
  /** Display-only fallback for unit and description. */
  unit?: string | null
  description?: string | null
}

export type RangePreset = 'last-5m' | 'last-15m' | 'last-1h' | 'last-6h' | 'last-24h'

export const RANGE_PRESETS: RangePreset[] = ['last-5m', 'last-15m', 'last-1h', 'last-6h', 'last-24h']

export interface BaseWidgetConfig {
  /** Optional override for the widget header. Default is taken from the registry. */
  title?: string
}

export interface MetricStatConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
  showSparkline: boolean
  /** Display-only override for the unit label suffix shown next to the
   *  numeric value. Used when `unitKind` is `'none'`; the kind-aware formatter
   *  already provides its own suffix otherwise. Default = metric.unit. */
  unit?: string | null
  /** Decimals to display (default 2). */
  decimals?: number
  /** Reduction applied to the loaded points to produce the displayed scalar.
   *  Default `'last'`. */
  calc?: CalcMode
  /** Auto-scaling formatter strategy. Default `'none'` (raw number). */
  unitKind?: UnitKind
  /** Value-driven coloring. Empty/absent = use the theme default. */
  thresholds?: ThresholdStop[]
}

export interface MetricLineConfig extends BaseWidgetConfig {
  metrics: MetricBinding[]
  range: RangePreset
  /** Attribute key to disaggregate by, or null for "all attributes". */
  splitBy?: string | null
  /** Override the auto-picked chart type (`pickChartType(...)`). */
  chartTypeOverride?: ChartType
  /** Auto-scaling formatter for axis labels and tooltips. Default `'none'`. */
  unitKind?: UnitKind
  /** Decimals for axis labels and tooltips (default 2). */
  decimals?: number
}

export interface MetricSparklineConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
  /** Auto-scaling formatter for the tooltip. Default `'none'`. */
  unitKind?: UnitKind
  /** Decimals for the tooltip (default 2). */
  decimals?: number
}

export interface MetricGaugeConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
  /** Reduction over the loaded points. Default `'last'`. */
  calc?: CalcMode
  /** Auto-scaling unit formatter. Default `'none'`. */
  unitKind?: UnitKind
  decimals?: number
  /** Inclusive scale boundaries. Defaults: 0 .. 100. */
  min?: number
  max?: number
  /** Optional thresholds — both color the needle and paint colored arcs on
   *  the gauge backdrop. Empty list = neutral grey arc. */
  thresholds?: ThresholdStop[]
}

export interface MetricBarGaugeConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
  /** Attribute key used to disaggregate the metric into one bar per group.
   *  Null = no split → a single bar that's only useful next to thresholds. */
  splitBy?: string | null
  /** Reduction collapsing each group's points to a scalar. Default `'last'`. */
  calc?: CalcMode
  unitKind?: UnitKind
  decimals?: number
  /** Cap on bars rendered after sorting (descending). Default 10. */
  topN?: number
  /** Inclusive scale boundaries used to size each bar. When `null`/absent the
   *  largest current value defines `max` (auto-fit). */
  min?: number
  max?: number | null
  thresholds?: ThresholdStop[]
}

export type TraceSortMode = 'recent' | 'slowest' | 'errors-first'

export interface RecentTracesConfig extends BaseWidgetConfig {
  range: RangePreset
  /** Restrict to a specific service.name; null = all services. */
  service?: string | null
  /** Sort applied client-side after fetching. Default `'recent'`. */
  sort?: TraceSortMode
  /** Cap on rows fetched/rendered (default 20). */
  limit?: number
}

export type LogSeverityFilter = 'all' | 'info' | 'warn' | 'error' | 'fatal'

export interface LogsStreamConfig extends BaseWidgetConfig {
  range: RangePreset
  /** Restrict to a specific service.name; null = all. */
  service?: string | null
  /** Minimum severity rendered. Default `'all'`. */
  minSeverity?: LogSeverityFilter
  /** Cap on rows fetched/rendered (default 50). */
  limit?: number
}

export interface MetricHeatmapConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
  /** Required for a meaningful heatmap — without it there's only one row.
   *  Null/empty falls back to a single aggregated row. */
  splitBy?: string | null
  /** Number of time buckets across the X axis. Default 24. */
  buckets?: number
  /** Reduction inside each bucket. Default `'mean'`. */
  bucketReduce?: CalcMode
  unitKind?: UnitKind
  decimals?: number
  /** Threshold-driven color mapping. When empty, falls back to a viridis-like
   *  gradient across the data range. */
  thresholds?: ThresholdStop[]
}

export interface MetricPieConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
  /** Attribute key used to slice the metric. Required for a meaningful pie —
   *  without it the chart degenerates to a single slice. */
  splitBy?: string | null
  /** Reduction per slice. Default `'last'`. */
  calc?: CalcMode
  unitKind?: UnitKind
  decimals?: number
  /** Render as a donut (inner ring) when true. Default false (full pie). */
  donut?: boolean
  /** Show legend below the chart. Default true. */
  showLegend?: boolean
}

export interface TextWidgetConfig extends BaseWidgetConfig {
  markdown: string
  align?: 'left' | 'center'
}

export type WidgetConfig =
  | MetricStatConfig
  | MetricLineConfig
  | MetricSparklineConfig
  | MetricGaugeConfig
  | MetricBarGaugeConfig
  | MetricPieConfig
  | MetricHeatmapConfig
  | RecentTracesConfig
  | LogsStreamConfig
  | TextWidgetConfig

/** A single widget instance: identity + grid coords + kind-specific config. */
export interface WidgetItem {
  id: string
  kind: WidgetKind
  x: number
  y: number
  w: number
  h: number
  config: WidgetConfig
}

/** Top-level layout shape persisted as JSON in the dashboard's `layoutJson`. */
export interface DashboardLayout {
  widgets: WidgetItem[]
}

/** Helper to type-narrow without `as` casts inside templates. */
export function isMetricStat(item: WidgetItem): item is WidgetItem & { config: MetricStatConfig } {
  return item.kind === 'metric-stat'
}
export function isMetricLine(item: WidgetItem): item is WidgetItem & { config: MetricLineConfig } {
  return item.kind === 'metric-line'
}
export function isMetricSparkline(
  item: WidgetItem
): item is WidgetItem & { config: MetricSparklineConfig } {
  return item.kind === 'metric-sparkline'
}
export function isText(item: WidgetItem): item is WidgetItem & { config: TextWidgetConfig } {
  return item.kind === 'text'
}
export function isMetricGauge(item: WidgetItem): item is WidgetItem & { config: MetricGaugeConfig } {
  return item.kind === 'metric-gauge'
}
export function isMetricBarGauge(
  item: WidgetItem
): item is WidgetItem & { config: MetricBarGaugeConfig } {
  return item.kind === 'metric-bar-gauge'
}
export function isMetricPie(item: WidgetItem): item is WidgetItem & { config: MetricPieConfig } {
  return item.kind === 'metric-pie'
}
export function isMetricHeatmap(
  item: WidgetItem
): item is WidgetItem & { config: MetricHeatmapConfig } {
  return item.kind === 'metric-heatmap'
}
export function isRecentTraces(
  item: WidgetItem
): item is WidgetItem & { config: RecentTracesConfig } {
  return item.kind === 'recent-traces'
}
export function isLogsStream(item: WidgetItem): item is WidgetItem & { config: LogsStreamConfig } {
  return item.kind === 'logs-stream'
}

/** Convenience: turn an InstrumentDto into a binding (for the picker UI). */
export function instrumentToBinding(instrument: InstrumentDto): MetricBinding {
  return {
    resourceHash: instrument.resourceHash,
    scopeName: instrument.scopeName,
    instrumentName: instrument.name,
    kind: instrument.kind,
    serviceName: instrument.serviceName,
    unit: instrument.unit,
    description: instrument.description
  }
}
