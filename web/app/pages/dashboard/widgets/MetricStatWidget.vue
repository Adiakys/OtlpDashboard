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

const props = withDefaults(defineProps<{
  config: MetricStatConfig
  isEditing: boolean
  liveTick: number
  preview?: boolean
}>(), { preview: false })

defineEmits<{
  edit: []
  remove: []
}>()

const { t, locale } = useI18n()
const { $metricsService } = useNuxtApp()
const colorMode = useColorMode()

const metrics = useSingleMetric(() => props.config.metric, () => props.config.parameters)
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
  if (aggregated.value === null) return '·'
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
  if (delta.value === null || delta.value === 0) return 'vellum-delta-zero'
  return delta.value > 0 ? 'vellum-delta-up' : 'vellum-delta-down'
})

const isDark = computed(() => colorMode.value === 'dark')

const matchedThreshold = computed(() => {
  if (aggregated.value === null) return null
  return pickThreshold(aggregated.value, thresholds.value)
})

const valueColor = computed<string | undefined>(() => matchedThreshold.value?.color)

const sparklineStroke = computed<string>(() => {
  if (matchedThreshold.value) return matchedThreshold.value.color
  // Ember accent — Vellum default series color.
  return isDark.value ? '#E8895C' : '#C9602F'
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
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <div class="vellum-preview-stat">
        <span class="vellum-preview-stat__value">42.0</span>
        <span class="vellum-preview-stat__unit">ms</span>
        <svg class="vellum-preview-stat__spark" viewBox="0 0 60 18" preserveAspectRatio="none">
          <polyline
            points="0,14 8,11 16,13 24,7 32,9 40,5 48,8 60,3"
            fill="none"
            stroke="currentColor"
            stroke-width="1.5"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </div>
    </template>
    <template #default="{ width, height }">
      <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-mono-sm text-muted px-3 text-center">
        {{ t('dashboard.widgets.notConfigured') }}
      </div>
      <div v-else class="flex-1 min-h-0 min-w-0 flex flex-col px-4 py-3 gap-1.5">
        <div class="flex items-baseline gap-2 leading-none min-w-0">
          <span
            class="truncate"
            :style="{
              fontFamily: 'var(--font-mono)',
              fontWeight: 500,
              letterSpacing: '-0.01em',
              fontVariantNumeric: 'tabular-nums',
              fontSize: `clamp(1.25rem, ${Math.round(height * 0.20)}px, 2.5rem)`,
              ...(valueColor ? { color: valueColor } : {})
            }"
          >{{ formattedValue }}</span>
          <span
            v-if="unitLabel && width > 140"
            class="text-overline truncate"
            style="color: var(--color-graphite-500);"
          >{{ unitLabel }}</span>
        </div>
        <div
          v-if="delta !== null && height >= 100"
          class="text-mono-sm shrink-0"
          :class="deltaTone"
          style="font-variant-numeric: tabular-nums;"
        >
          Δ {{ deltaLabel }}
        </div>
        <div v-if="config.showSparkline && sortedPoints.length > 1 && height >= 140" class="flex-1 min-h-0 min-w-0">
          <AppChart :options="sparkOptions" />
        </div>
      </div>
    </template>
  </BaseWidget>
</template>

<style scoped>
.vellum-delta-zero { color: var(--color-graphite-500); }
.vellum-delta-up   { color: var(--color-sage-600); }
.vellum-delta-down { color: var(--color-rust-600); }
:global(html.dark) .vellum-delta-up   { color: var(--color-sage-400); }
:global(html.dark) .vellum-delta-down { color: var(--color-rust-400); }

.vellum-preview-stat {
  flex: 1;
  display: flex;
  align-items: baseline;
  gap: 0.4rem;
  padding: 0.4rem 0.6rem;
  font-family: var(--font-mono);
  font-variant-numeric: tabular-nums;
}
.vellum-preview-stat__value {
  font-size: clamp(1rem, 2.4vw, 1.5rem);
  font-weight: 500;
  color: var(--color-ember-700);
  letter-spacing: -0.01em;
}
:global(html.dark) .vellum-preview-stat__value { color: var(--color-ember-400); }
.vellum-preview-stat__unit {
  font-size: 0.7rem;
  color: var(--color-graphite-500);
}
.vellum-preview-stat__spark {
  flex: 1;
  height: 1.6rem;
  color: var(--color-ember-500);
}
</style>
