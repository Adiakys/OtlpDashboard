<script setup lang="ts">
import type { AgChartOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import type { MetricStatConfig } from '../types'
import { WIDGET_METADATA } from '../registry'
import { reduce, type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { pickThreshold } from '~/lib/units/thresholds'

const props = defineProps<{
  config: MetricStatConfig
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
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_METADATA['metric-stat'].titleKey)
)

const sortedPoints = computed(() => {
  const ps = series.value[0]?.points ?? []
  return [...ps].sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())
})

const calc = computed<CalcMode>(() => props.config.calc ?? 'last')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)
const thresholds = computed(() => props.config.thresholds ?? [])

const aggregated = computed<number | null>(() => {
  return reduce(sortedPoints.value.map(p => Number(p.value)), calc.value)
})

const previous = computed(() => {
  // For the delta we always compare the last two raw points — `calc` describes
  // the displayed scalar, but the trend pill stays meaningful only against the
  // immediately preceding sample.
  const ps = sortedPoints.value
  return ps.length >= 2 ? ps[ps.length - 2]! : null
})

const latest = computed(() => sortedPoints.value.at(-1) ?? null)

const formattedValue = computed(() => {
  if (aggregated.value === null) return '—'
  if (unitKind.value === 'none') {
    // Preserve the legacy `unit` text suffix when the formatter doesn't
    // contribute its own.
    return formatValue(aggregated.value, 'none', { decimals: decimals.value, locale: locale.value })
  }
  return formatValue(aggregated.value, unitKind.value, { decimals: decimals.value, locale: locale.value })
})

const unitLabel = computed(() => {
  // Only show the manual unit suffix when the formatter is `'none'` —
  // otherwise the formatter already prints "MB", "ms", "%", etc.
  if (unitKind.value !== 'none') return ''
  return props.config.unit ?? props.config.metric?.unit ?? ''
})

const delta = computed(() => {
  if (latest.value === null || previous.value === null) return null
  return Number(latest.value.value) - Number(previous.value.value)
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

const matchedThreshold = computed(() => {
  if (aggregated.value === null) return null
  return pickThreshold(aggregated.value, thresholds.value)
})

const valueColor = computed<string | undefined>(() => matchedThreshold.value?.color)

const sparklineStroke = computed<string>(() => {
  if (matchedThreshold.value) return matchedThreshold.value.color
  return isDark.value ? '#5eead4' : '#0d9488'
})

const sparkOptions = computed<AgChartOptions>(() => ({
  data: sortedPoints.value.map(p => ({ time: new Date(p.time), value: Number(p.value) })),
  series: [{ type: 'line', xKey: 'time', yKey: 'value', stroke: sparklineStroke.value, strokeWidth: 2, marker: { enabled: false } }],
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
    <template #default="{ width, height }">
      <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
        {{ t('dashboard.widgets.notConfigured') }}
      </div>
      <div v-else class="flex-1 min-h-0 min-w-0 flex flex-col p-3 gap-2">
        <div class="flex items-baseline gap-2 leading-none min-w-0">
          <span
            class="font-semibold tabular-nums truncate"
            :class="height < 120 ? 'text-xl' : height < 200 ? 'text-2xl' : 'text-3xl'"
            :style="valueColor ? { color: valueColor } : undefined"
          >{{ formattedValue }}</span>
          <span v-if="unitLabel && width > 140" class="text-sm text-muted truncate">{{ unitLabel }}</span>
        </div>
        <div v-if="delta !== null && height >= 100" class="text-xs tabular-nums shrink-0" :class="deltaTone">
          Δ {{ deltaLabel }}
        </div>
        <div v-if="config.showSparkline && sortedPoints.length > 1 && height >= 140" class="flex-1 min-h-0 min-w-0">
          <AppChart :options="sparkOptions" />
        </div>
      </div>
    </template>
  </BaseWidget>
</template>
