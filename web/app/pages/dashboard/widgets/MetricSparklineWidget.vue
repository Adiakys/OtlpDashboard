<script setup lang="ts">
import type { AgChartOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import type { MetricSparklineConfig } from '../types'
import { WIDGET_METADATA } from '../registry'
import { formatValue, type UnitKind } from '~/lib/units/format'

const props = defineProps<{
  config: MetricSparklineConfig
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
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_METADATA['metric-sparkline'].titleKey)
)

const sortedPoints = computed(() => {
  const ps = series.value[0]?.points ?? []
  return [...ps].sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())
})

const isDark = computed(() => colorMode.value === 'dark')

const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)

interface SparkParams {
  datum: { time: Date; value: number }
  yValue?: number
}
function tooltipRenderer(params: SparkParams): { title?: string; content: string } {
  const v = typeof params.yValue === 'number' ? params.yValue : Number(params.datum.value)
  const time = params.datum.time
  const timeLabel = time.toLocaleTimeString(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
  const formatted = formatValue(v, unitKind.value, { decimals: decimals.value, locale: locale.value })
  return { content: `<b>${escapeHtml(formatted)}</b><br/>${escapeHtml(timeLabel)}` }
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
  }[c] as string))
}

const options = computed<AgChartOptions>(() => ({
  data: sortedPoints.value.map(p => ({ time: new Date(p.time), value: Number(p.value) })),
  series: [{
    type: 'area',
    xKey: 'time',
    yKey: 'value',
    fillOpacity: 0.25,
    fill: isDark.value ? '#5eead4' : '#0d9488',
    stroke: isDark.value ? '#5eead4' : '#0d9488',
    strokeWidth: 2,
    marker: { enabled: false },
    tooltip: { renderer: tooltipRenderer }
  }],
  axes: [
    { type: 'time', position: 'bottom', label: { enabled: false }, line: { enabled: false }, tick: { enabled: false }, gridLine: { enabled: false } },
    { type: 'number', position: 'left', label: { enabled: false }, line: { enabled: false }, tick: { enabled: false }, gridLine: { enabled: false } }
  ],
  background: { visible: false },
  padding: { top: 4, right: 4, bottom: 4, left: 4 },
  legend: { enabled: false }
}))

const isConfigured = computed(() => props.config.metric !== null)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_METADATA['metric-sparkline'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
      {{ t('dashboard.widgets.notConfigured') }}
    </div>
    <div v-else class="flex-1 min-h-0 min-w-0">
      <AppChart :options="options" />
    </div>
  </BaseWidget>
</template>
