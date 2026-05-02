<script setup lang="ts">
import { computed } from 'vue'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { buildChartOptions, pickChartType, type ChartType } from '~/lib/agcharts/chartStrategy'
import { useWidgetSeries } from '../useWidgetSeries'
import { normalizeSplitBy } from '../composables/normalizeSplitBy'
import { expandMetricBindings } from '~/lib/htmlEngine/parameterExpansion'
import type { MetricLineConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { formatValue, type UnitKind } from '~/lib/units/format'

const props = withDefaults(defineProps<{
  config: MetricLineConfig
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

// Expand `${param}` placeholders in each binding's logical-key fields
// against the per-instance parameters map. Bindings whose required
// fields collapse to empty are dropped — the resulting array still
// drives the chart cleanly without firing 404-bound requests.
const metrics = computed(() => expandMetricBindings(props.config.metrics, props.config.parameters))
const range = computed(() => props.config.range)
const { series, loading, error, hasLoaded } = useWidgetSeries(
  $metricsService, metrics, range, () => props.liveTick,
  { includeAttributes: true }
)

const headerTitle = computed(() => {
  if (props.config.title) return props.config.title
  if (props.config.metrics.length === 1) return props.config.metrics[0]!.instrumentName
  if (props.config.metrics.length > 1) return t('dashboard.widgets.metricLine.titleMulti', { n: props.config.metrics.length })
  return t(WIDGET_REGISTRY['metric-line'].titleKey)
})

const chartType = computed<ChartType>(() => {
  if (props.config.chartTypeOverride) return props.config.chartTypeOverride
  const head = props.config.metrics[0]
  if (!head) return 'line'
  // Need temporality + isMonotonic, which the binding doesn't carry — fall
  // back to the loaded series' instrument metadata (it does).
  const matching = series.value.find(
    s =>
      s.instrument.resourceHash === head.resourceHash &&
      s.instrument.scopeName === head.scopeName &&
      s.instrument.name === head.instrumentName &&
      s.instrument.kind === head.kind
  )
  if (!matching) return 'line'
  return pickChartType(matching.instrument.kind, matching.instrument.temporality, matching.instrument.isMonotonic)
})

const splitBy = computed(() => normalizeSplitBy(props.config.splitBy))
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)

const valueFormatter = computed<(v: number) => string>(() => {
  const kind = unitKind.value
  const dec = decimals.value
  const loc = locale.value
  return (v: number) => formatValue(v, kind, { decimals: dec, locale: loc })
})

function optionsFor(width: number, height: number) {
  // Switch to a stripped-down chart (no legend, no axis labels/grid) when
  // the widget is short or narrow — AG Charts otherwise reserves so much
  // space for legend + axes that the plot collapses to a few pixels.
  const compact = height < 180 || width < 260
  return buildChartOptions({
    series: series.value,
    chartType: chartType.value,
    splitBy: splitBy.value,
    locale: locale.value,
    isDark: colorMode.value === 'dark',
    compact,
    valueFormatter: valueFormatter.value
  })
}

const isConfigured = computed(() => props.config.metrics.length > 0)
const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['metric-line'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :show-skeleton="showSkeleton"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <svg class="vellum-preview-line" viewBox="0 0 100 36" preserveAspectRatio="none">
        <polyline points="0,28 14,22 28,25 42,15 56,18 70,9 84,12 100,5"
          fill="none" stroke="var(--color-ember-500)" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>
        <polyline points="0,32 14,29 28,30 42,24 56,26 70,22 84,21 100,18"
          fill="none" stroke="var(--color-graphite-500)" stroke-width="1.4" stroke-opacity="0.55" stroke-linecap="round" stroke-linejoin="round"/>
        <polyline points="0,18 14,16 28,17 42,12 56,14 70,8 84,10 100,4"
          fill="none" stroke="var(--color-sage-500)" stroke-width="1.4" stroke-opacity="0.7" stroke-linecap="round" stroke-linejoin="round"/>
      </svg>
    </template>
    <template #default="{ width, height }">
      <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
        {{ t('dashboard.widgets.notConfigured') }}
      </div>
      <div v-else class="flex-1 min-h-0 min-w-0">
        <AppChart :options="optionsFor(width, height)" />
      </div>
    </template>
  </BaseWidget>
</template>

<style scoped>
.vellum-preview-line {
  flex: 1;
  height: 100%;
  padding: 0.4rem 0.6rem;
}
</style>
