import type { ChartType } from '~/lib/agcharts/chartStrategy'
import type { CalcMode } from '~/lib/units/calc'
import type { ThresholdStop } from '~/lib/units/thresholds'
import type { UnitKind } from '~/lib/units/format'
import type { InstrumentDto } from '~/services/types'

export type { CalcMode, ThresholdStop, UnitKind }

/**
 * Builtin widget kinds shipped in the bundle. Each entry has a Vue
 * component + config form in `STD_DEFINITIONS`. New builtin kinds extend
 * the union *and* the catalog static map.
 */
export type BuiltinKind =
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

/** @deprecated Use `BuiltinKind`. Kept as alias to avoid sweeping renames. */
export type WidgetKind = BuiltinKind

/**
 * Widget source attribution. Carried implicitly inside the `kind` string
 * persisted on every `WidgetItem`:
 *   - `std:<builtinKind>`           (bundled, e.g. `std:metric-stat`)
 *   - `custom:<uuid>`               (user-saved definition, DB-backed)
 *   - `library:<libraryId>/<kindId>` (filesystem / git-installed library)
 *
 * The runtime parses the prefix via `parseKind()`; persistence stays opaque.
 */
export type WidgetSource = 'std' | 'custom' | { library: string }

/**
 * Fully-qualified kind string. A bare builtin kind (`metric-stat`) is also
 * accepted at parse time and treated as `std:metric-stat` for backward
 * compatibility with dashboards saved before the FQ scheme.
 */
export type FQKind = string

/** Engine the renderer dispatches on for a widget definition. */
export type WidgetEngine = 'preset' | 'spec' | 'composite'

/**
 * A widget definition — the *recipe*, not the *instance*. Lives in the
 * catalog (`useWidgetCatalog()`). `std` definitions are static, `custom`
 * come from the server, library ones from the filesystem registry.
 */
export interface WidgetDefinition {
  /** Fully-qualified kind, used by `WidgetItem.kind` to reference this def. */
  kind: FQKind
  source: WidgetSource
  /** Display label for the picker. */
  name: string
  /** Optional description shown alongside the name. */
  description?: string
  /** Phosphor / Lucide icon class. */
  icon: string
  engine: WidgetEngine
  defaultSize: { w: number; h: number }
  /** For `engine === 'preset'`: the bare builtin kind being wrapped
   *  (`metric-stat`, `gauge`, …). Always *unprefixed* — the FQ form lives
   *  in the parent `kind` field. */
  baseKind?: BuiltinKind
  /** For `engine === 'preset'`: the seed config the picker copies into the
   *  new instance. Other engines may use this for engine-specific knobs. */
  defaultConfig?: WidgetConfig
  /** For `engine === 'spec'` / `'composite'`: the engine-specific spec
   *  (Vega-Lite spec, composite layout DSL). Wired in iter 2/5. */
  spec?: unknown
}

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

/**
 * A single widget instance: identity + grid coords + kind-specific config.
 * `kind` is a fully-qualified string (`std:<builtin>` / `custom:<uuid>` /
 * `library:<libId>/<kindId>`); legacy bare-kind values are accepted by the
 * compat layer (`normalizeKind`) and treated as `std:<bare>`.
 */
export interface WidgetItem {
  id: string
  kind: FQKind
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

// =============================================================
// FQ-kind parsing & normalization
//
// The `kind` string carries source attribution as a prefix:
//   "std:metric-stat"
//   "custom:6f2b1f21-7c42-4a4e-9b3a-e0f0a5d8f1c2"
//   "library:team-otel-pack/sla-tracker"
//
// Bare builtin kinds (`metric-stat`) are accepted at parse time for
// backward compatibility with dashboards saved before this scheme — the
// backend migration `NormalizeWidgetKindsToFqn` rewrites them at rest, and
// `normalizeKind()` mirrors the same rule client-side at load.
// =============================================================

export interface ParsedKind {
  source: WidgetSource
  /** Builtin kind id (`metric-stat`), custom uuid, or `<libId>/<kindId>`. */
  id: string
}

export function parseKind(kind: FQKind): ParsedKind {
  const colon = kind.indexOf(':')
  if (colon < 0) {
    // Legacy bare-kind: assume builtin.
    return { source: 'std', id: kind }
  }

  const prefix = kind.slice(0, colon)
  const rest = kind.slice(colon + 1)

  if (prefix === 'std') return { source: 'std', id: rest }
  if (prefix === 'custom') return { source: 'custom', id: rest }
  if (prefix === 'library') {
    // "library:<libId>/<kindId>" — keep the slash form on the id so callers
    // can route to a specific library entry.
    const slash = rest.indexOf('/')
    const libId = slash < 0 ? rest : rest.slice(0, slash)
    return { source: { library: libId }, id: rest }
  }
  // Unknown prefix: treat as opaque builtin so the renderer falls back to
  // the "widget not available" placeholder rather than crashing.
  return { source: 'std', id: kind }
}

/** Returns the FQ form of any input — idempotent on already-prefixed values. */
export function normalizeKind(kind: string): FQKind {
  if (kind.includes(':')) return kind
  return `std:${kind}`
}

/**
 * Format a parsed kind back into its FQ string. Inverse of `parseKind`.
 */
export function formatKind(parsed: ParsedKind): FQKind {
  if (parsed.source === 'std') return `std:${parsed.id}`
  if (parsed.source === 'custom') return `custom:${parsed.id}`
  return `library:${parsed.id}`
}

/**
 * Type-narrow on a builtin kind. Each guard inspects `parseKind(item.kind)`,
 * so it transparently handles both the FQ form and any unmigrated legacy
 * bare-kind values that slipped through.
 */
function isBuiltin(item: WidgetItem, builtin: BuiltinKind): boolean {
  const parsed = parseKind(item.kind)
  return parsed.source === 'std' && parsed.id === builtin
}

export function isMetricStat(item: WidgetItem): item is WidgetItem & { config: MetricStatConfig } {
  return isBuiltin(item, 'metric-stat')
}
export function isMetricLine(item: WidgetItem): item is WidgetItem & { config: MetricLineConfig } {
  return isBuiltin(item, 'metric-line')
}
export function isMetricSparkline(
  item: WidgetItem
): item is WidgetItem & { config: MetricSparklineConfig } {
  return isBuiltin(item, 'metric-sparkline')
}
export function isText(item: WidgetItem): item is WidgetItem & { config: TextWidgetConfig } {
  return isBuiltin(item, 'text')
}
export function isMetricGauge(item: WidgetItem): item is WidgetItem & { config: MetricGaugeConfig } {
  return isBuiltin(item, 'metric-gauge')
}
export function isMetricBarGauge(
  item: WidgetItem
): item is WidgetItem & { config: MetricBarGaugeConfig } {
  return isBuiltin(item, 'metric-bar-gauge')
}
export function isMetricPie(item: WidgetItem): item is WidgetItem & { config: MetricPieConfig } {
  return isBuiltin(item, 'metric-pie')
}
export function isMetricHeatmap(
  item: WidgetItem
): item is WidgetItem & { config: MetricHeatmapConfig } {
  return isBuiltin(item, 'metric-heatmap')
}
export function isRecentTraces(
  item: WidgetItem
): item is WidgetItem & { config: RecentTracesConfig } {
  return isBuiltin(item, 'recent-traces')
}
export function isLogsStream(item: WidgetItem): item is WidgetItem & { config: LogsStreamConfig } {
  return isBuiltin(item, 'logs-stream')
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
