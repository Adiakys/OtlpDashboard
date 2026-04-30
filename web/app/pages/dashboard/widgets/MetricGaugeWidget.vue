<script setup lang="ts">
import { computed } from 'vue'
import BaseWidget from '../components/BaseWidget.vue'
import { useWidgetSeries } from '../useWidgetSeries'
import { useSingleMetric } from '../composables/useSingleMetric'
import { useReducedScalar } from '../composables/useReducedScalar'
import type { MetricGaugeConfig } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { type CalcMode } from '~/lib/units/calc'
import { formatValue, type UnitKind } from '~/lib/units/format'
import { pickThreshold } from '~/lib/units/thresholds'

const props = withDefaults(defineProps<{
  config: MetricGaugeConfig
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

const metrics = useSingleMetric(() => props.config.metric)
const range = computed(() => props.config.range)
const { series, loading, error, hasLoaded } = useWidgetSeries(
  $metricsService, metrics, range, () => props.liveTick
)

const headerTitle = computed(() =>
  props.config.title || props.config.metric?.instrumentName || t(WIDGET_REGISTRY['metric-gauge'].titleKey)
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

const aggregated = useReducedScalar(series, calc)

const matchedThreshold = computed(() => {
  if (aggregated.value === null) return null
  return pickThreshold(aggregated.value, thresholds.value)
})

const valueColor = computed<string>(() => {
  if (matchedThreshold.value) return matchedThreshold.value.color
  // Vellum ember accent.
  return colorMode.value === 'dark' ? '#E8895C' : '#C9602F'
})

// Track tone aligns with warm graphite neutral (was cool zinc).
const trackColor = computed<string>(() => colorMode.value === 'dark' ? '#2a2823' : '#e8e5dd')

const formattedValue = computed(() => {
  if (aggregated.value === null) return '·'
  return formatValue(aggregated.value, unitKind.value, { decimals: decimals.value, locale: locale.value })
})

const formattedMin = computed(() => formatValue(minValue.value, unitKind.value, { decimals: 0, locale: locale.value }))
const formattedMax = computed(() => formatValue(maxValue.value, unitKind.value, { decimals: 0, locale: locale.value }))

// Arc geometry — sweeps 270° from -135° to +135° (bottom-open). All paths are
// drawn in a 200×200 viewBox; the SVG is then scaled to fit the widget body
// while preserving aspect ratio. Font sizes are also expressed in viewBox
// units so the gauge text scales cleanly with the container.
const ARC_START_DEG = -225 // = -135° measured from 12 o'clock, in standard math coords
const ARC_END_DEG = 45     // sweep 270° clockwise
const RADIUS = 80
const CENTER = 100
const STROKE_WIDTH = 16
// Cap viewport units so the value-text doesn't overflow narrow widgets.
const VALUE_FONT_SIZE = 22
const SCALE_FONT_SIZE = 10

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
  const start = polarToCartesian(CENTER, CENTER, RADIUS, startDeg)
  const end = polarToCartesian(CENTER, CENTER, RADIUS, endDeg)
  const sweep = Math.abs(endDeg - startDeg)
  const largeArcFlag = sweep > 180 ? 1 : 0
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
const showSkeleton = computed(() => isConfigured.value && !hasLoaded.value && loading.value)
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['metric-gauge'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :show-skeleton="showSkeleton"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <div class="vellum-preview-gauge">
        <svg viewBox="0 0 100 60" class="vellum-preview-gauge__svg">
          <!-- track -->
          <path d="M 10 55 A 40 40 0 0 1 90 55" fill="none" stroke="currentColor" stroke-opacity="0.18" stroke-width="6" stroke-linecap="round"/>
          <!-- value arc — fills ~70% of the half circle -->
          <path d="M 10 55 A 40 40 0 0 1 78 22" fill="none" stroke="var(--color-ember-500)" stroke-width="6" stroke-linecap="round"/>
        </svg>
        <span class="vellum-preview-gauge__value">72%</span>
      </div>
    </template>
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
          <!-- Min / Max labels (font in viewBox units → scales with the widget) -->
          <text x="40" y="170" class="fill-(--ui-text-muted)" :style="{ fontSize: SCALE_FONT_SIZE + 'px' }" text-anchor="middle">{{ formattedMin }}</text>
          <text x="160" y="170" class="fill-(--ui-text-muted)" :style="{ fontSize: SCALE_FONT_SIZE + 'px' }" text-anchor="middle">{{ formattedMax }}</text>
          <!-- Value -->
          <text
            x="100"
            y="105"
            text-anchor="middle"
            class="font-semibold tabular-nums"
            :style="{ fill: valueColor, fontSize: VALUE_FONT_SIZE + 'px' }"
          >{{ formattedValue }}</text>
        </svg>
      </div>
    </template>
  </BaseWidget>
</template>

<style scoped>
.vellum-preview-gauge {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.15rem;
  padding: 0.4rem 0.6rem;
  color: var(--color-graphite-500);
}
.vellum-preview-gauge__svg {
  width: 70%;
  height: auto;
}
.vellum-preview-gauge__value {
  font-family: var(--font-mono);
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--color-ember-700);
  font-variant-numeric: tabular-nums;
}
:global(html.dark) .vellum-preview-gauge__value { color: var(--color-ember-400); }
</style>
