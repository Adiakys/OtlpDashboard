<script setup lang="ts">
import { computed } from 'vue'
import BaseWidget from '../components/BaseWidget.vue'
import WidgetWarningChip from '../components/WidgetWarningChip.vue'
import { useWidgetSeries, presetToWindow } from '../useWidgetSeries'
import { useSingleMetric } from '../composables/useSingleMetric'
import { normalizeSplitBy } from '../composables/normalizeSplitBy'
import type { MetricHeatmapConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { reduce, type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { pickThreshold } from '~/lib/units/thresholds'
import { describeGroup, groupPoints } from '~/lib/agcharts/seriesGrouping'

const props = withDefaults(defineProps<{
  config: MetricHeatmapConfig
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
const { series, loading, error, warnings, hasLoaded } = useWidgetSeries(
  $metricsService, metrics, range, () => props.liveTick,
  { includeAttributes: true }
)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_REGISTRY['metric-heatmap'].titleKey)
)

const buckets = computed(() => Math.max(4, Math.min(120, props.config.buckets ?? 24)))
const bucketReduce = computed<CalcMode>(() => props.config.bucketReduce ?? 'mean')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)
const thresholds = computed(() => props.config.thresholds ?? [])

const splitBy = computed(() => normalizeSplitBy(props.config.splitBy))

interface Cell {
  value: number | null
  fraction: number
  color: string
}

interface Row {
  label: string
  cells: Cell[]
}

interface HeatmapData {
  rows: Row[]
  bucketStarts: number[]
  minValue: number
  maxValue: number
}

const heatmap = computed<HeatmapData>(() => {
  const points = series.value[0]?.points ?? []
  const window = presetToWindow(props.config.range)
  const fromMs = new Date(window.from).getTime()
  const toMs = new Date(window.to).getTime()
  const span = toMs - fromMs
  if (span <= 0 || points.length === 0) {
    return { rows: [], bucketStarts: [], minValue: 0, maxValue: 0 }
  }

  const bucketCount = buckets.value
  const bucketWidth = span / bucketCount
  const bucketStarts: number[] = []
  for (let i = 0; i < bucketCount; i++) bucketStarts.push(fromMs + i * bucketWidth)

  const groups = groupPoints(points, splitBy.value)

  // Phase 1 — bin each group into the time buckets, then reduce per bucket.
  const rawRows: { label: string; perBucket: (number | null)[] }[] = []
  let globalMin = Number.POSITIVE_INFINITY
  let globalMax = Number.NEGATIVE_INFINITY
  for (const g of groups) {
    const buckets: number[][] = Array.from({ length: bucketCount }, () => [])
    for (const p of g.points) {
      const tMs = new Date(p.time).getTime()
      const idx = Math.min(bucketCount - 1, Math.max(0, Math.floor((tMs - fromMs) / bucketWidth)))
      const v = Number(p.value)
      if (Number.isFinite(v)) buckets[idx]!.push(v)
    }
    const reduced: (number | null)[] = buckets.map(values => reduce(values, bucketReduce.value))
    for (const r of reduced) {
      if (r === null) continue
      if (r < globalMin) globalMin = r
      if (r > globalMax) globalMax = r
    }
    rawRows.push({ label: describeGroup(g.attrs), perBucket: reduced })
  }
  if (!Number.isFinite(globalMin) || !Number.isFinite(globalMax)) {
    return { rows: [], bucketStarts, minValue: 0, maxValue: 0 }
  }

  // Phase 2 — color cells. If any thresholds are configured we paint by stop;
  // otherwise we use a teal-to-warm gradient over the observed range.
  const colorRange = globalMax - globalMin || 1
  const fallback = colorMode.value === 'dark'
  const rows: Row[] = rawRows.map(r => ({
    label: r.label,
    cells: r.perBucket.map(v => {
      if (v === null) return { value: null, fraction: 0, color: 'transparent' }
      const fraction = (v - globalMin) / colorRange
      const matched = pickThreshold(v, thresholds.value)
      const color = matched ? matched.color : gradientColor(fraction, fallback)
      return { value: v, fraction, color }
    })
  }))

  return { rows, bucketStarts, minValue: globalMin, maxValue: globalMax }
})

/** Linear interpolation between two perceptually-distinct anchors. Light mode
 *  uses pale-teal → red; dark mode uses deep-teal → red so the cells stay
 *  legible against a dark canvas. */
function gradientColor(t: number, dark: boolean): string {
  const tt = Math.min(1, Math.max(0, t))
  const start = dark ? [13, 42, 56] : [220, 252, 231]    // muted teal vs. mint
  const mid = dark ? [22, 163, 162] : [13, 148, 136]      // teal-500
  const end = dark ? [239, 68, 68] : [220, 38, 38]        // red
  const lerp = (a: number, b: number, x: number) => Math.round(a + (b - a) * x)
  let rgb: [number, number, number]
  if (tt < 0.5) {
    const x = tt / 0.5
    rgb = [lerp(start[0]!, mid[0]!, x), lerp(start[1]!, mid[1]!, x), lerp(start[2]!, mid[2]!, x)]
  } else {
    const x = (tt - 0.5) / 0.5
    rgb = [lerp(mid[0]!, end[0]!, x), lerp(mid[1]!, end[1]!, x), lerp(mid[2]!, end[2]!, x)]
  }
  return `rgb(${rgb[0]} ${rgb[1]} ${rgb[2]})`
}

function formatBucketTime(ms: number): string {
  return new Date(ms).toLocaleTimeString(locale.value, { hour: '2-digit', minute: '2-digit' })
}

function tooltipFor(rowLabel: string, c: Cell, bucketMs: number): string {
  if (c.value === null) return `${rowLabel}\n${formatBucketTime(bucketMs)}\n—`
  const v = formatValue(c.value, unitKind.value, { decimals: decimals.value, locale: locale.value })
  return `${rowLabel}\n${formatBucketTime(bucketMs)}\n${v}`
}

const isConfigured = computed(() => props.config.metric !== null)
const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['metric-heatmap'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :show-skeleton="showSkeleton"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #header-end>
      <WidgetWarningChip :warnings="warnings" />
    </template>
    <template #preview>
      <div class="vellum-preview-heatmap">
        <span v-for="(c, i) in 24" :key="i"
          class="vellum-preview-heatmap__cell"
          :style="{ '--i': i }" />
      </div>
    </template>
    <div v-if="!isConfigured" class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center">
      {{ t('dashboard.widgets.notConfigured') }}
    </div>
    <div
      v-else-if="heatmap.rows.length === 0"
      class="flex-1 min-h-0 flex items-center justify-center text-xs text-muted px-3 text-center"
    >
      {{ t('dashboard.widgets.noData') }}
    </div>
    <div v-else class="flex-1 min-h-0 min-w-0 overflow-auto p-2">
      <div class="flex flex-col gap-1 min-w-fit">
        <div
          v-for="(row, ri) in heatmap.rows"
          :key="ri"
          class="flex items-center gap-1"
        >
          <!--
            Label column: responsive width (max-w-40 on >=md, max-w-28 below)
            instead of a hard `w-32`. Long attribute keys get a horizontal
            scrollbar via `overflow-auto` on the container, not a hard truncate.
          -->
          <div class="text-[10px] text-muted truncate max-w-40 min-w-24 shrink-0" :title="row.label">{{ row.label }}</div>
          <div class="flex gap-px flex-1 min-w-0">
            <div
              v-for="(c, ci) in row.cells"
              :key="ci"
              class="h-5 flex-1 min-w-[6px] rounded-sm"
              :style="{ background: c.color }"
              :title="tooltipFor(row.label, c, heatmap.bucketStarts[ci] ?? 0)"
            />
          </div>
        </div>
        <!-- X axis ticks: first / mid / last bucket start. -->
        <div class="flex items-center gap-1 mt-1">
          <div class="max-w-40 min-w-24 shrink-0" />
          <div class="flex-1 min-w-0 flex justify-between text-[10px] text-muted tabular-nums">
            <span>{{ formatBucketTime(heatmap.bucketStarts[0] ?? 0) }}</span>
            <span>
              {{ formatBucketTime(heatmap.bucketStarts[Math.floor(heatmap.bucketStarts.length / 2)] ?? 0) }}
            </span>
            <span>{{ formatBucketTime(heatmap.bucketStarts[heatmap.bucketStarts.length - 1] ?? 0) }}</span>
          </div>
        </div>
      </div>
    </div>
  </BaseWidget>
</template>

<style scoped>
.vellum-preview-heatmap {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(8, 1fr);
  grid-template-rows: repeat(3, 1fr);
  gap: 2px;
  padding: 0.5rem;
}
.vellum-preview-heatmap__cell {
  background: var(--color-ember-500);
  border-radius: 2px;
  /* Vary opacity per cell index so the grid reads as a heatmap, not a
     solid block. The pattern below is hand-tuned for 24 cells. */
  opacity: calc(0.18 + 0.7 * (((var(--i) * 53) % 100) / 100));
}
</style>
