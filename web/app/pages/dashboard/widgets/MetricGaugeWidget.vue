<script setup lang="ts">
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import type { MetricGaugeConfig } from '../types'
import { WIDGET_METADATA } from '../registry'
import { reduce, type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { pickThreshold } from '~/lib/units/thresholds'

const props = defineProps<{
  config: MetricGaugeConfig
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
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_METADATA['metric-gauge'].titleKey)
)

const calc = computed<CalcMode>(() => props.config.calc ?? 'last')
const unitKind = computed<UnitKind>(() => props.config.unitKind ?? 'none')
const decimals = computed(() => props.config.decimals ?? 2)
const thresholds = computed(() => props.config.thresholds ?? [])
const minValue = computed(() => props.config.min ?? 0)
const maxValue = computed(() => {
  const m = props.config.max
  return Number.isFinite(m) && (m as number) > minValue.value ? (m as number) : minValue.value + 100
})

const aggregated = computed<number | null>(() => {
  const points = series.value[0]?.points ?? []
  return reduce(points.map(p => Number(p.value)), calc.value)
})

const matchedThreshold = computed(() => {
  if (aggregated.value === null) return null
  return pickThreshold(aggregated.value, thresholds.value)
})

const valueColor = computed<string>(() => {
  if (matchedThreshold.value) return matchedThreshold.value.color
  return colorMode.value === 'dark' ? '#5eead4' : '#0d9488'
})

const trackColor = computed<string>(() => colorMode.value === 'dark' ? '#27272a' : '#e4e4e7')

const formattedValue = computed(() => {
  if (aggregated.value === null) return '—'
  return formatValue(aggregated.value, unitKind.value, { decimals: decimals.value, locale: locale.value })
})

const formattedMin = computed(() => formatValue(minValue.value, unitKind.value, { decimals: 0, locale: locale.value }))
const formattedMax = computed(() => formatValue(maxValue.value, unitKind.value, { decimals: 0, locale: locale.value }))

// Arc geometry — sweeps 270° from -135° to +135° (bottom-open). All paths are
// drawn in a 200×200 viewBox; the SVG is then scaled to fit the widget body
// while preserving aspect ratio.
const ARC_START_DEG = -225 // = -135° measured from 12 o'clock, in standard math coords
const ARC_END_DEG = 45     // sweep 270° clockwise
const RADIUS = 80
const CENTER = 100
const STROKE_WIDTH = 16

const fraction = computed<number>(() => {
  if (aggregated.value === null) return 0
  const span = maxValue.value - minValue.value
  if (span <= 0) return 0
  return Math.min(1, Math.max(0, (aggregated.value - minValue.value) / span))
})

const valueArc = computed(() => arcPath(ARC_START_DEG, lerpDeg(ARC_START_DEG, ARC_END_DEG, fraction.value)))
const trackArc = computed(() => arcPath(ARC_START_DEG, ARC_END_DEG))

interface ThresholdBand {
  d: string
  color: string
}

const thresholdBands = computed<ThresholdBand[]>(() => {
  const stops = [...thresholds.value].sort((a, b) => a.value - b.value)
  if (stops.length === 0) return []
  const bands: ThresholdBand[] = []
  const span = maxValue.value - minValue.value
  if (span <= 0) return []
  for (let i = 0; i < stops.length; i++) {
    const startVal = i === 0 ? minValue.value : stops[i]!.value
    const endVal = i === stops.length - 1 ? maxValue.value : stops[i + 1]!.value
    const startFrac = clamp01((startVal - minValue.value) / span)
    const endFrac = clamp01((endVal - minValue.value) / span)
    if (endFrac <= startFrac) continue
    bands.push({
      d: arcPath(lerpDeg(ARC_START_DEG, ARC_END_DEG, startFrac), lerpDeg(ARC_START_DEG, ARC_END_DEG, endFrac)),
      color: stops[i]!.color
    })
  }
  return bands
})

function arcPath(startDeg: number, endDeg: number): string {
  // SVG arc — large-arc-flag depends on sweep > 180°.
  const start = polarToCartesian(CENTER, CENTER, RADIUS, startDeg)
  const end = polarToCartesian(CENTER, CENTER, RADIUS, endDeg)
  const sweep = Math.abs(endDeg - startDeg)
  const largeArcFlag = sweep > 180 ? 1 : 0
  // sweepFlag=1 (clockwise in screen coords because Y is inverted)
  return `M ${start.x.toFixed(2)} ${start.y.toFixed(2)} A ${RADIUS} ${RADIUS} 0 ${largeArcFlag} 1 ${end.x.toFixed(2)} ${end.y.toFixed(2)}`
}

function polarToCartesian(cx: number, cy: number, r: number, angleDeg: number): { x: number; y: number } {
  const rad = (angleDeg * Math.PI) / 180
  return { x: cx + r * Math.cos(rad), y: cy + r * Math.sin(rad) }
}

function lerpDeg(a: number, b: number, t: number): number {
  return a + (b - a) * t
}

function clamp01(v: number): number {
  return Math.min(1, Math.max(0, v))
}

const isConfigured = computed(() => props.config.metric !== null)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_METADATA['metric-gauge'].icon"
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
      <div v-else class="flex-1 min-h-0 min-w-0 flex items-center justify-center p-2">
        <svg
          viewBox="0 0 200 200"
          preserveAspectRatio="xMidYMid meet"
          class="block"
          :style="{ width: Math.min(width, height) + 'px', height: Math.min(width, height) + 'px' }"
        >
          <!-- Background track. Hidden when threshold bands cover the arc. -->
          <path
            v-if="thresholdBands.length === 0"
            :d="trackArc"
            :stroke="trackColor"
            :stroke-width="STROKE_WIDTH"
            fill="none"
            stroke-linecap="round"
          />
          <!-- Threshold bands (faded) -->
          <path
            v-for="(b, i) in thresholdBands"
            :key="i"
            :d="b.d"
            :stroke="b.color"
            :stroke-width="STROKE_WIDTH"
            fill="none"
            stroke-linecap="butt"
            opacity="0.25"
          />
          <!-- Foreground value arc -->
          <path
            v-if="fraction > 0"
            :d="valueArc"
            :stroke="valueColor"
            :stroke-width="STROKE_WIDTH"
            fill="none"
            stroke-linecap="round"
          />
          <!-- Min / Max labels -->
          <text x="40" y="170" class="text-[10px] fill-(--ui-text-muted)" text-anchor="middle">{{ formattedMin }}</text>
          <text x="160" y="170" class="text-[10px] fill-(--ui-text-muted)" text-anchor="middle">{{ formattedMax }}</text>
          <!-- Value -->
          <text
            x="100"
            y="105"
            text-anchor="middle"
            class="font-semibold tabular-nums"
            :style="{ fill: valueColor, fontSize: '24px' }"
          >{{ formattedValue }}</text>
        </svg>
      </div>
    </template>
  </BaseWidget>
</template>
