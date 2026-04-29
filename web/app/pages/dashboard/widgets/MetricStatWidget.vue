<script setup lang="ts">
import { computed } from 'vue'
import type { AgChartOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import { useSingleMetric } from '../composables/useSingleMetric'
import { useReducedScalar } from '../composables/useReducedScalar'
import type { MetricStatConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { type CalcMode } from '~/lib/units/calc'
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

const metrics = useSingleMetric(() => props.config.metric)
const range = computed(() => props.config.range)
const { series, loading, error, hasLoaded } = useWidgetSeries(
  $metricsService, metrics, range, () => props.liveTick
)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_REGISTRY['metric-stat'].titleKey)
)

const sortedPoints = computed(() => {
  // groupPoints (used by widgets that split by attribute) sorts internally,
  // but Stat reads the raw series and needs a guarantee for the delta calc.
  const ps = series.value[0]?.points ?? []
  return [...ps].sort((a, b) => new Date(a.time).getTime() - new Date(b.time).getTime())
})

const calc = computed<CalcMode>(() => props.config.calc ?? 'last')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)
const thresholds = computed(() => props.config.thresholds ?? [])

const aggregated = useReducedScalar(series, calc)

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
const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['metric-stat'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :show-skeleton="showSkeleton"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #default="{ width, height }">
      <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
        {{ t('dashboard.widgets.notConfigured') }}
      </div>
      <div v-else class="flex-1 min-h-0 min-w-0 flex flex-col p-3 gap-2">
        <div class="flex items-baseline gap-2 leading-none min-w-0">
          <!--
            Use clamp() to scale the value smoothly with the widget height
            instead of jumping between text-xl/2xl/3xl tiers. The lower bound
            keeps the digit legible on tiny widgets; the upper bound caps the
            growth so a tall narrow widget doesn't run off horizontally.
          -->
          <span
            class="font-semibold tabular-nums truncate"
            :style="{
              fontSize: `clamp(1.125rem, ${Math.round(height * 0.18)}px, 2.5rem)`,
              ...(valueColor ? { color: valueColor } : {})
            }"
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
