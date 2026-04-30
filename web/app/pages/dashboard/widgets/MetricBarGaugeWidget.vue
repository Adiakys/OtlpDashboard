<script setup lang="ts">
import { computed } from 'vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import { useSingleMetric } from '../composables/useSingleMetric'
import { normalizeSplitBy } from '../composables/normalizeSplitBy'
import type { MetricBarGaugeConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { reduce, type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { pickThreshold } from '~/lib/units/thresholds'
import { describeGroup, groupPoints } from '~/lib/agcharts/seriesGrouping'

const props = defineProps<{
  config: MetricBarGaugeConfig
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
  $metricsService, metrics, range, () => props.liveTick,
  { includeAttributes: true }
)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_REGISTRY['metric-bar-gauge'].titleKey)
)

const calc = computed<CalcMode>(() => props.config.calc ?? 'last')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)
const thresholds = computed(() => props.config.thresholds ?? [])
const topN = computed(() => Math.max(1, Math.min(50, props.config.topN ?? 10)))
const minValue = computed(() => props.config.min ?? 0)

const splitBy = computed(() => normalizeSplitBy(props.config.splitBy))

interface Bar {
  key: string
  label: string
  value: number
  color: string
  fraction: number
  formatted: string
}

const bars = computed<Bar[]>(() => {
  const points = series.value[0]?.points ?? []
  if (points.length === 0) return []
  const groups = groupPoints(points, splitBy.value)
  const reduced: { key: string; label: string; value: number }[] = []
  for (const g of groups) {
    const v = reduce(g.points.map(p => Number(p.value)), calc.value)
    if (v === null) continue
    reduced.push({ key: g.key, label: describeGroup(g.attrs), value: v })
  }
  reduced.sort((a, b) => b.value - a.value)
  const top = reduced.slice(0, topN.value)

  // Decide max: explicit config wins, else largest visible value (or 1 to avoid /0).
  const explicitMax = props.config.max
  const autoMax = top.length > 0 ? Math.max(...top.map(b => b.value)) : 1
  const max = Number.isFinite(explicitMax) && (explicitMax as number) > minValue.value
    ? (explicitMax as number)
    : autoMax
  const span = max - minValue.value || 1

  const fallback = colorMode.value === 'dark' ? '#E8895C' : '#C9602F'
  return top.map(b => {
    const matched = pickThreshold(b.value, thresholds.value)
    return {
      key: b.key,
      label: b.label,
      value: b.value,
      color: matched?.color ?? fallback,
      fraction: Math.min(1, Math.max(0, (b.value - minValue.value) / span)),
      formatted: formatValue(b.value, unitKind.value, { decimals: decimals.value, locale: locale.value })
    }
  })
})

const isConfigured = computed(() => props.config.metric !== null)
const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['metric-bar-gauge'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :show-skeleton="showSkeleton"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #default>
      <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
        {{ t('dashboard.widgets.notConfigured') }}
      </div>
      <div
        v-else-if="bars.length === 0"
        class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center"
      >
        {{ t('dashboard.widgets.noData') }}
      </div>
      <div v-else class="flex-1 min-h-0 min-w-0 overflow-auto p-3 flex flex-col gap-2">
        <div v-for="b in bars" :key="b.key" class="flex flex-col gap-1 min-w-0">
          <div class="flex items-baseline justify-between gap-2 min-w-0">
            <span class="text-xs text-muted truncate" :title="b.label">{{ b.label }}</span>
            <span
              class="text-xs font-semibold tabular-nums shrink-0"
              :style="{ color: b.color }"
            >{{ b.formatted }}</span>
          </div>
          <div class="h-2 rounded bg-elevated overflow-hidden">
            <div
              class="h-full rounded transition-all duration-300"
              :style="{ width: (b.fraction * 100) + '%', background: b.color }"
            />
          </div>
        </div>
      </div>
    </template>
  </BaseWidget>
</template>
