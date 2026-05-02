<script setup lang="ts">
import { computed } from 'vue'
import type { AgChartOptions, AgPieSeriesOptions, AgDonutSeriesOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import { useSingleMetric } from '../composables/useSingleMetric'
import { normalizeSplitBy } from '../composables/normalizeSplitBy'
import type { MetricPieConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { reduce, type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { describeGroup, groupPoints } from '~/lib/agcharts/seriesGrouping'
import { escapeHtml } from '~/lib/escapeHtml'

const props = withDefaults(defineProps<{
  config: MetricPieConfig
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
  $metricsService, metrics, range, () => props.liveTick,
  { includeAttributes: true }
)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_REGISTRY['metric-pie'].titleKey)
)

const calc = computed<CalcMode>(() => props.config.calc ?? 'last')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)

const splitBy = computed(() => normalizeSplitBy(props.config.splitBy))

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
const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['metric-pie'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :show-skeleton="showSkeleton"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <svg class="vellum-preview-pie" viewBox="0 0 36 36">
        <!-- Stroke-dasharray ring math: circumference = 2π·r ≈ 100.5 with r=16. -->
        <circle cx="18" cy="18" r="16" fill="none" stroke-width="6"
          stroke="var(--color-ember-500)" stroke-dasharray="42 58.5" transform="rotate(-90 18 18)" />
        <circle cx="18" cy="18" r="16" fill="none" stroke-width="6"
          stroke="var(--color-graphite-500)" stroke-opacity="0.55" stroke-dasharray="28 72.5" stroke-dashoffset="-42" transform="rotate(-90 18 18)" />
        <circle cx="18" cy="18" r="16" fill="none" stroke-width="6"
          stroke="var(--color-sage-500)" stroke-opacity="0.65" stroke-dasharray="30.5 70" stroke-dashoffset="-70" transform="rotate(-90 18 18)" />
      </svg>
    </template>
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

<style scoped>
.vellum-preview-pie {
  flex: 1;
  margin: 0.3rem auto;
  max-height: 100%;
  height: auto;
  width: auto;
  aspect-ratio: 1 / 1;
}
</style>
