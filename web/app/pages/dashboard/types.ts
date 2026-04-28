import type { ChartType } from '~/lib/agcharts/chartStrategy'
import type { InstrumentDto } from '~/services/types'

/** Widget kinds shipped in v1. New kinds extend the union and the registry. */
export type WidgetKind = 'metric-stat' | 'metric-line' | 'metric-sparkline' | 'text'

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
  /** Override the metric's unit; default = metric.unit. */
  unit?: string | null
  /** Decimals to display (default 2). */
  decimals?: number
}

export interface MetricLineConfig extends BaseWidgetConfig {
  metrics: MetricBinding[]
  range: RangePreset
  /** Attribute key to disaggregate by, or null for "all attributes". */
  splitBy?: string | null
  /** Override the auto-picked chart type (`pickChartType(...)`). */
  chartTypeOverride?: ChartType
}

export interface MetricSparklineConfig extends BaseWidgetConfig {
  metric: MetricBinding | null
  range: RangePreset
}

export interface TextWidgetConfig extends BaseWidgetConfig {
  markdown: string
  align?: 'left' | 'center'
}

export type WidgetConfig =
  | MetricStatConfig
  | MetricLineConfig
  | MetricSparklineConfig
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
