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
  TextWidgetConfig,
  WidgetConfig,
  WidgetKind
} from './types'

/**
 * Static metadata for each widget kind: i18n keys for header/picker labels,
 * default size on the 12-column grid, and a factory for the empty config.
 *
 * The kind → component mapping (and the kind → config-form mapping) are
 * resolved in the page that imports them — keeping them out of this module
 * avoids a circular import between the registry and the Vue SFCs.
 */
export interface WidgetKindMetadata {
  titleKey: string
  descKey: string
  icon: string
  defaultSize: { w: number; h: number }
}

export const WIDGET_METADATA: Record<WidgetKind, WidgetKindMetadata> = {
  'metric-stat': {
    titleKey: 'dashboard.widgets.metricStat.title',
    descKey: 'dashboard.widgets.metricStat.desc',
    icon: 'i-lucide-gauge',
    defaultSize: { w: 3, h: 3 }
  },
  'metric-line': {
    titleKey: 'dashboard.widgets.metricLine.title',
    descKey: 'dashboard.widgets.metricLine.desc',
    icon: 'i-lucide-line-chart',
    defaultSize: { w: 6, h: 4 }
  },
  'metric-sparkline': {
    titleKey: 'dashboard.widgets.metricSparkline.title',
    descKey: 'dashboard.widgets.metricSparkline.desc',
    icon: 'i-lucide-activity',
    defaultSize: { w: 3, h: 2 }
  },
  'metric-gauge': {
    titleKey: 'dashboard.widgets.metricGauge.title',
    descKey: 'dashboard.widgets.metricGauge.desc',
    icon: 'i-lucide-gauge-circle',
    defaultSize: { w: 3, h: 3 }
  },
  'metric-bar-gauge': {
    titleKey: 'dashboard.widgets.metricBarGauge.title',
    descKey: 'dashboard.widgets.metricBarGauge.desc',
    icon: 'i-lucide-bar-chart-horizontal',
    defaultSize: { w: 4, h: 4 }
  },
  'metric-pie': {
    titleKey: 'dashboard.widgets.metricPie.title',
    descKey: 'dashboard.widgets.metricPie.desc',
    icon: 'i-lucide-pie-chart',
    defaultSize: { w: 4, h: 4 }
  },
  'metric-heatmap': {
    titleKey: 'dashboard.widgets.metricHeatmap.title',
    descKey: 'dashboard.widgets.metricHeatmap.desc',
    icon: 'i-lucide-grid-3x3',
    defaultSize: { w: 6, h: 4 }
  },
  'recent-traces': {
    titleKey: 'dashboard.widgets.recentTraces.title',
    descKey: 'dashboard.widgets.recentTraces.desc',
    icon: 'i-lucide-list',
    defaultSize: { w: 6, h: 4 }
  },
  'logs-stream': {
    titleKey: 'dashboard.widgets.logsStream.title',
    descKey: 'dashboard.widgets.logsStream.desc',
    icon: 'i-lucide-scroll-text',
    defaultSize: { w: 6, h: 4 }
  },
  text: {
    titleKey: 'dashboard.widgets.text.title',
    descKey: 'dashboard.widgets.text.desc',
    icon: 'i-lucide-type',
    defaultSize: { w: 4, h: 2 }
  }
}

export const WIDGET_KINDS: WidgetKind[] = [
  'metric-stat',
  'metric-gauge',
  'metric-line',
  'metric-bar-gauge',
  'metric-sparkline',
  'metric-pie',
  'metric-heatmap',
  'recent-traces',
  'logs-stream',
  'text'
]

export function defaultSizeFor(kind: WidgetKind): { w: number; h: number } {
  return WIDGET_METADATA[kind].defaultSize
}

export function defaultConfigFor(kind: WidgetKind): WidgetConfig {
  switch (kind) {
    case 'metric-stat':
      return {
        metric: null,
        range: 'last-1h',
        showSparkline: true,
        decimals: 2
      } satisfies MetricStatConfig
    case 'metric-line':
      return {
        metrics: [],
        range: 'last-1h',
        splitBy: null
      } satisfies MetricLineConfig
    case 'metric-sparkline':
      return {
        metric: null,
        range: 'last-1h'
      } satisfies MetricSparklineConfig
    case 'metric-gauge':
      return {
        metric: null,
        range: 'last-1h',
        calc: 'last',
        unitKind: 'none',
        decimals: 2,
        min: 0,
        max: 100,
        thresholds: []
      } satisfies MetricGaugeConfig
    case 'metric-bar-gauge':
      return {
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
      } satisfies MetricBarGaugeConfig
    case 'metric-pie':
      return {
        metric: null,
        range: 'last-1h',
        splitBy: null,
        calc: 'last',
        unitKind: 'none',
        decimals: 2,
        donut: false,
        showLegend: true
      } satisfies MetricPieConfig
    case 'metric-heatmap':
      return {
        metric: null,
        range: 'last-1h',
        splitBy: null,
        buckets: 24,
        bucketReduce: 'mean',
        unitKind: 'none',
        decimals: 2,
        thresholds: []
      } satisfies MetricHeatmapConfig
    case 'recent-traces':
      return {
        range: 'last-1h',
        service: null,
        sort: 'recent',
        limit: 20
      } satisfies RecentTracesConfig
    case 'logs-stream':
      return {
        range: 'last-15m',
        service: null,
        minSeverity: 'all',
        limit: 50
      } satisfies LogsStreamConfig
    case 'text':
      return {
        markdown: '## Nuovo pannello\n\nScrivi qui…',
        align: 'left'
      } satisfies TextWidgetConfig
  }
}
