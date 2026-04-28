<script setup lang="ts">
import type { AgChartOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import type { MetricStatConfig } from '../types'
import { WIDGET_METADATA } from '../registry'

const props = defineProps<{
  config: MetricStatConfig
  isEditing: boolean
  liveTick: Readonly<Ref<number>>
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
const { series, loading, error } = useWidgetSeries($metricsService, metrics, range, props.liveTick)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_METADATA['metric-stat'].titleKey)
)

const sortedPoints = computed(() => {
  const ps = series.value[0]?.points ?? []
  return [...ps].sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())
})

const latest = computed(() => sortedPoints.value.at(-1) ?? null)
const previous = computed(() => {
  const ps = sortedPoints.value
  return ps.length >= 2 ? ps[ps.length - 2]! : null
})

const decimals = computed(() => props.config.decimals ?? 2)

const formattedValue = computed(() => {
  if (latest.value === null) return '—'
  return new Intl.NumberFormat(locale.value, {
    maximumFractionDigits: decimals.value,
    minimumFractionDigits: decimals.value
  }).format(latest.value.value)
})

const unitLabel = computed(() => props.config.unit ?? props.config.metric?.unit ?? '')

const delta = computed(() => {
  if (latest.value === null || previous.value === null) return null
  return latest.value.value - previous.value.value
})

const deltaLabel = computed(() => {
  if (delta.value === null) return ''
  const fmt = new Intl.NumberFormat(locale.value, {
    maximumFractionDigits: decimals.value,
    minimumFractionDigits: 0,
    signDisplay: 'always'
  })
  return fmt.format(delta.value)
})

const deltaTone = computed(() => {
  if (delta.value === null || delta.value === 0) return 'text-muted'
  return delta.value > 0 ? 'text-success' : 'text-error'
})

const isDark = computed(() => colorMode.value === 'dark')

const sparkOptions = computed<AgChartOptions>(() => ({
  data: sortedPoints.value.map(p => ({ time: new Date(p.time), value: Number(p.value) })),
  series: [{ type: 'line', xKey: 'time', yKey: 'value', stroke: isDark.value ? '#5eead4' : '#0d9488', strokeWidth: 2, marker: { enabled: false } }],
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
    :icon="WIDGET_METADATA['metric-stat'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <div v-if="!isConfigured" class="h-full flex items-center justify-center text-xs text-muted px-3 text-center">
      {{ t('dashboard.widgets.notConfigured') }}
    </div>
    <div v-else class="h-full flex flex-col p-3 gap-2 min-h-0">
      <div class="flex items-baseline gap-2 leading-none">
        <span class="text-3xl font-semibold tabular-nums truncate">{{ formattedValue }}</span>
        <span v-if="unitLabel" class="text-sm text-muted truncate">{{ unitLabel }}</span>
      </div>
      <div v-if="delta !== null" class="text-xs tabular-nums" :class="deltaTone">
        Δ {{ deltaLabel }}
      </div>
      <div v-if="config.showSparkline && sortedPoints.length > 1" class="flex-1 min-h-0">
        <AppChart :options="sparkOptions" />
      </div>
    </div>
  </BaseWidget>
</template>
