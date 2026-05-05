import { defineAsyncComponent, type Component } from 'vue'
import type {
  LogsStreamConfig,
  MetricBarGaugeConfig,
  MetricGaugeConfig,
  MetricHeatmapConfig,
  MetricLineConfig,
  MetricPieConfig,
  MetricSparklineConfig,
  MetricStatConfig,
  RecentTracesConfig,
  TopTracesConfig,
  TextWidgetConfig,
  WidgetConfig,
  WidgetKind
} from './types'

/**
 * Static metadata + lazy component bindings for every widget kind. The
 * `defineAsyncComponent` calls are evaluated lazily, so adding a new kind
 * never grows the initial bundle. The single source of truth for both the
 * grid renderer (`WidgetSlot`) and the config drawer (`WidgetConfigSlot`).
 *
 * Adding a new widget = add one entry here + write the two SFCs. Nothing
 * else in the dashboard module needs to know.
 */
export interface WidgetKindMetadata {
  titleKey: string
  descKey: string
  icon: string
  defaultSize: { w: number; h: number }
  /** The widget renderer mounted inside the grid cell. */
  component: Component
  /** The form mounted inside the config drawer. */
  configForm: Component
  /** Empty config used when the user adds a fresh widget of this kind. */
  defaultConfig: () => WidgetConfig
  /**
   * True when the widget exposes a `<template #preview>` slot. The picker
   * mounts the component with `:preview="true"` only for kinds with this
   * flag set, so kinds without an authored preview don't pay the
   * mount-cost or surface an empty container.
   */
  hasPreview?: boolean
}

export const WIDGET_REGISTRY: Record<WidgetKind, WidgetKindMetadata> = {
  'metric-stat': {
    titleKey: 'dashboard.widgets.metricStat.title',
    descKey: 'dashboard.widgets.metricStat.desc',
    icon: 'i-ph-gauge',
    defaultSize: { w: 3, h: 3 },
    component: defineAsyncComponent(() => import('./widgets/MetricStatWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/StatConfigForm.vue')),
    defaultConfig: () => ({
      metric: null,
      range: 'last-1h',
      showSparkline: true,
      decimals: 2
    } satisfies MetricStatConfig),
    hasPreview: true
  },
  'metric-line': {
    titleKey: 'dashboard.widgets.metricLine.title',
    descKey: 'dashboard.widgets.metricLine.desc',
    icon: 'i-ph-chart-line',
    defaultSize: { w: 6, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/MetricLineWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/LineConfigForm.vue')),
    defaultConfig: () => ({
      metrics: [],
      range: 'last-1h',
      splitBy: null
    } satisfies MetricLineConfig),
    hasPreview: true
  },
  'metric-sparkline': {
    titleKey: 'dashboard.widgets.metricSparkline.title',
    descKey: 'dashboard.widgets.metricSparkline.desc',
    icon: 'i-ph-pulse',
    defaultSize: { w: 3, h: 2 },
    component: defineAsyncComponent(() => import('./widgets/MetricSparklineWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/SparklineConfigForm.vue')),
    defaultConfig: () => ({
      metric: null,
      range: 'last-1h'
    } satisfies MetricSparklineConfig),
    hasPreview: true
  },
  'metric-gauge': {
    titleKey: 'dashboard.widgets.metricGauge.title',
    descKey: 'dashboard.widgets.metricGauge.desc',
    icon: 'i-ph-speedometer',
    defaultSize: { w: 3, h: 3 },
    component: defineAsyncComponent(() => import('./widgets/MetricGaugeWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/GaugeConfigForm.vue')),
    defaultConfig: () => ({
      metric: null,
      range: 'last-1h',
      calc: 'last',
      unitKind: 'none',
      decimals: 2,
      min: 0,
      max: 100,
      thresholds: []
    } satisfies MetricGaugeConfig),
    hasPreview: true
  },
  'metric-bar-gauge': {
    titleKey: 'dashboard.widgets.metricBarGauge.title',
    descKey: 'dashboard.widgets.metricBarGauge.desc',
    icon: 'i-ph-chart-bar',
    defaultSize: { w: 4, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/MetricBarGaugeWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/BarGaugeConfigForm.vue')),
    defaultConfig: () => ({
      metric: null,
      range: 'last-1h',
      splitBy: null,
      calc: 'last',
      unitKind: 'none',
      decimals: 2,
      topN: 10,
      min: 0,
      max: null,
      thresholds: []
    } satisfies MetricBarGaugeConfig),
    hasPreview: true
  },
  'metric-pie': {
    titleKey: 'dashboard.widgets.metricPie.title',
    descKey: 'dashboard.widgets.metricPie.desc',
    icon: 'i-ph-chart-pie',
    defaultSize: { w: 4, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/MetricPieWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/PieConfigForm.vue')),
    defaultConfig: () => ({
      metric: null,
      range: 'last-1h',
      splitBy: null,
      calc: 'last',
      unitKind: 'none',
      decimals: 2,
      donut: false,
      showLegend: true
    } satisfies MetricPieConfig),
    hasPreview: true
  },
  'metric-heatmap': {
    titleKey: 'dashboard.widgets.metricHeatmap.title',
    descKey: 'dashboard.widgets.metricHeatmap.desc',
    icon: 'i-ph-grid-four',
    defaultSize: { w: 6, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/MetricHeatmapWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/HeatmapConfigForm.vue')),
    defaultConfig: () => ({
      metric: null,
      range: 'last-1h',
      splitBy: null,
      buckets: 24,
      bucketReduce: 'mean',
      unitKind: 'none',
      decimals: 2,
      thresholds: []
    } satisfies MetricHeatmapConfig),
    hasPreview: true
  },
  'recent-traces': {
    titleKey: 'dashboard.widgets.recentTraces.title',
    descKey: 'dashboard.widgets.recentTraces.desc',
    icon: 'i-ph-list',
    defaultSize: { w: 6, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/RecentTracesWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/RecentTracesConfigForm.vue')),
    defaultConfig: () => ({
      range: 'last-1h',
      service: null,
      sort: 'recent',
      limit: 20
    } satisfies RecentTracesConfig),
    hasPreview: true
  },
  'top-traces': {
    titleKey: 'dashboard.widgets.topTraces.title',
    descKey: 'dashboard.widgets.topTraces.desc',
    icon: 'i-ph-trophy',
    defaultSize: { w: 6, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/TopTracesWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/TopTracesConfigForm.vue')),
    defaultConfig: () => ({
      range: 'last-1h',
      service: null,
      metric: 'count',
      limit: 10
    } satisfies TopTracesConfig),
    hasPreview: true
  },
  'logs-stream': {
    titleKey: 'dashboard.widgets.logsStream.title',
    descKey: 'dashboard.widgets.logsStream.desc',
    icon: 'i-ph-scroll',
    defaultSize: { w: 6, h: 4 },
    component: defineAsyncComponent(() => import('./widgets/LogsStreamWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/LogsStreamConfigForm.vue')),
    defaultConfig: () => ({
      range: 'last-15m',
      service: null,
      minSeverity: 'all',
      limit: 50
    } satisfies LogsStreamConfig),
    hasPreview: true
  },
  text: {
    titleKey: 'dashboard.widgets.text.title',
    descKey: 'dashboard.widgets.text.desc',
    icon: 'i-ph-text-aa',
    defaultSize: { w: 4, h: 2 },
    component: defineAsyncComponent(() => import('./widgets/TextWidget.vue')),
    configForm: defineAsyncComponent(() => import('./configs/TextConfigForm.vue')),
    defaultConfig: () => ({
      markdown: '## Nuovo pannello\n\nScrivi qui…',
      align: 'left'
    } satisfies TextWidgetConfig),
    hasPreview: true
  }
}

/** Display order in the picker dialog. Most-used first. */
export const WIDGET_KINDS: WidgetKind[] = [
  'metric-stat',
  'metric-gauge',
  'metric-line',
  'metric-bar-gauge',
  'metric-sparkline',
  'metric-pie',
  'metric-heatmap',
  'recent-traces',
  'top-traces',
  'logs-stream',
  'text'
]

export function defaultSizeFor(kind: WidgetKind): { w: number; h: number } {
  return WIDGET_REGISTRY[kind].defaultSize
}

export function defaultConfigFor(kind: WidgetKind): WidgetConfig {
  return WIDGET_REGISTRY[kind].defaultConfig()
}
