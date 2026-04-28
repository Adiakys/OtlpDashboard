import type {
  MetricLineConfig,
  MetricSparklineConfig,
  MetricStatConfig,
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
  text: {
    titleKey: 'dashboard.widgets.text.title',
    descKey: 'dashboard.widgets.text.desc',
    icon: 'i-lucide-type',
    defaultSize: { w: 4, h: 2 }
  }
}

export const WIDGET_KINDS: WidgetKind[] = ['metric-stat', 'metric-line', 'metric-sparkline', 'text']

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
    case 'text':
      return {
        markdown: '## Nuovo pannello\n\nScrivi qui…',
        align: 'left'
      } satisfies TextWidgetConfig
  }
}
