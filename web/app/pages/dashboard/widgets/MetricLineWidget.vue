<script setup lang="ts">
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { buildChartOptions, pickChartType, type ChartType } from '~/lib/agcharts/chartStrategy'
import type { SplitBy } from '~/lib/agcharts/seriesGrouping'
import { useWidgetSeries } from '../useWidgetSeries'
import type { MetricLineConfig } from '../types'
import { WIDGET_METADATA } from '../registry'
import { formatValue, type UnitKind } from '~/lib/units/format'

const props = defineProps<{
  config: MetricLineConfig
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

const metrics = computed(() => props.config.metrics ?? [])
const range = computed(() => props.config.range)
const { series, loading, error } = useWidgetSeries($metricsService, metrics, range, () => props.liveTick)

const headerTitle = computed(() => {
  if (props.config.title) return props.config.title
  if (props.config.metrics.length === 1) return props.config.metrics[0]!.instrumentName
  if (props.config.metrics.length > 1) return t('dashboard.widgets.metricLine.titleMulti', { n: props.config.metrics.length })
  return t(WIDGET_METADATA['metric-line'].titleKey)
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

const splitBy = computed<SplitBy>(() => {
  const raw = props.config.splitBy
  if (!raw) return 'all'
  return [raw]
})

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
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_METADATA['metric-line'].icon"
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
      <div v-else class="flex-1 min-h-0 min-w-0">
        <AppChart :options="optionsFor(width, height)" />
      </div>
    </template>
  </BaseWidget>
</template>
