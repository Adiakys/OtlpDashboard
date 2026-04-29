<script setup lang="ts">
import type { AgChartOptions, AgPieSeriesOptions, AgDonutSeriesOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import type { MetricPieConfig } from '../types'
import { WIDGET_METADATA } from '../registry'
import { reduce, type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { describeGroup, groupPoints, type SplitBy } from '~/lib/agcharts/seriesGrouping'

const props = defineProps<{
  config: MetricPieConfig
  isEditing: boolean
  liveTick: number
}>()

defineEmits<{
  edit: []
  remove: []
}>()

const { t, locale } = useI18n()
const { $metricsService } = useNuxtApp()
const colorMode = useColorMode()

const metrics = computed(() => (props.config.metric ? [props.config.metric] : []))
const range = computed(() => props.config.range)
const { series, loading, error } = useWidgetSeries($metricsService, metrics, range, () => props.liveTick)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_METADATA['metric-pie'].titleKey)
)

const calc = computed<CalcMode>(() => props.config.calc ?? 'last')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)

const splitBy = computed<SplitBy>(() => {
  // Default to "all attributes" so a metric with attributes (e.g.
  // gc.collections by generation) renders as multiple slices out of the box.
  // The `splitBy` field then narrows to a single attribute key when set.
  const raw = props.config.splitBy
  if (!raw) return 'all'
  return [raw]
})

interface Slice {
  label: string
  value: number
}

const slices = computed<Slice[]>(() => {
  const points = series.value[0]?.points ?? []
  if (points.length === 0) return []
  const groups = groupPoints(points, splitBy.value)
  const out: Slice[] = []
  for (const g of groups) {
    const v = reduce(g.points.map(p => Number(p.value)), calc.value)
    if (v === null || v <= 0) continue
    out.push({ label: describeGroup(g.attrs), value: v })
  }
  return out
})

interface PieDatumParams {
  datum: Slice
  angleValue?: number
}

function pieTooltip(params: PieDatumParams): { content: string } {
  const v = typeof params.angleValue === 'number' ? params.angleValue : params.datum.value
  const formatted = formatValue(v, unitKind.value, { decimals: decimals.value, locale: locale.value })
  return { content: `<b>${escapeHtml(params.datum.label)}</b><br/>${escapeHtml(formatted)}` }
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c] as string))
}

const options = computed<AgChartOptions>(() => {
  const isDark = colorMode.value === 'dark'
  const showLegend = props.config.showLegend !== false
  const isDonut = props.config.donut === true

  const baseSeries = {
    angleKey: 'value',
    sectorLabelKey: undefined,
    calloutLabelKey: undefined,
    legendItemKey: 'label',
    tooltip: { renderer: pieTooltip }
  }

  const series: AgPieSeriesOptions | AgDonutSeriesOptions = isDonut
    ? { ...baseSeries, type: 'donut', innerRadiusRatio: 0.6 } as AgDonutSeriesOptions
    : { ...baseSeries, type: 'pie' } as AgPieSeriesOptions

  return {
    theme: isDark ? 'ag-default-dark' : 'ag-default',
    data: slices.value,
    series: [series],
    legend: { enabled: showLegend, position: 'bottom' },
    background: { visible: false },
    padding: { top: 4, right: 4, bottom: 4, left: 4 }
  }
})

const isConfigured = computed(() => props.config.metric !== null)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_METADATA['metric-pie'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
      {{ t('dashboard.widgets.notConfigured') }}
    </div>
    <div
      v-else-if="slices.length === 0"
      class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center"
    >
      {{ t('dashboard.widgets.noData') }}
    </div>
    <div v-else class="flex-1 min-h-0 min-w-0">
      <AppChart :options="options" />
    </div>
  </BaseWidget>
</template>
