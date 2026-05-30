<script setup lang="ts">
/**
 * Stacked-column histogram of log volume per severity over time. Pure
 * renderer: it takes a <see cref="SeverityHistogramData"/> and paints
 * SVG. Source-agnostic by design — switching from in-memory bucketing
 * to a server-side aggregation endpoint is a swap of the composable
 * the parent page wires up, not a change here.
 *
 * Why SVG over AG Charts: the chart is purely rectangles + a few text
 * labels; AG Charts would carry a chart-library overhead for what
 * fits in ~50 lines of declarative SVG. The output is also crisp at
 * any zoom level (no canvas blur on retina displays).
 */
import type { SeverityBucket } from '~/types/filters'
import { STACK_ORDER, type SeverityHistogramData } from '../composables/useSeverityHistogram'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const props = defineProps<{
  data: SeverityHistogramData
}>()

const { t, locale } = useI18n()

// Severity → CSS variable. The grey stack at the bottom carries the
// info/debug/trace bulk; warn/error/fatal pop with the accent palette.
// Pulled from `--color-*` tokens so dark mode works out of the box.
const SEVERITY_COLOR: Record<SeverityBucket, string> = {
  trace: 'var(--color-graphite-400)',
  debug: 'var(--color-graphite-500)',
  info:  'var(--color-graphite-600)',
  warn:  'var(--color-amber-500)',
  error: 'var(--color-rust-500)',
  fatal: 'var(--color-rust-700)'
}

// SVG viewport — width is a percentage so the chart stretches with
// the panel; height is fixed in px because the chart is decorative
// alongside the grid (we don't want it to dominate the layout).
const VIEWBOX_WIDTH = 1000
const PLOT_HEIGHT = 44

const maxCount = computed(() =>
  props.data.buckets.reduce((m, b) => (b.total > m ? b.total : m), 0)
)

const columnGap = 1 // px in viewBox units; thin separator
const columnWidth = computed(() => {
  const n = Math.max(1, props.data.buckets.length)
  return Math.max(1, (VIEWBOX_WIDTH - columnGap * (n - 1)) / n)
})

interface RenderedSlice {
  fill: string
  yPx: number
  heightPx: number
}
interface RenderedColumn {
  xPx: number
  startMs: number
  endMs: number
  total: number
  slices: RenderedSlice[]
  /** Pre-built tooltip so hover doesn't recompute. */
  title: string
}

function fmtTime(ms: number): string {
  return dateTimeFormat(ms, 'time-seconds', locale.value)
}

const columns = computed<RenderedColumn[]>(() => {
  const max = maxCount.value
  const w = columnWidth.value
  if (max === 0) return []
  return props.data.buckets.map((bucket, idx) => {
    const slices: RenderedSlice[] = []
    let yCursor = PLOT_HEIGHT // start at the bottom and walk upward
    for (const sev of STACK_ORDER) {
      const count = bucket.counts[sev] ?? 0
      if (count === 0) continue
      const heightPx = (count / max) * PLOT_HEIGHT
      yCursor -= heightPx
      slices.push({ fill: SEVERITY_COLOR[sev], yPx: yCursor, heightPx })
    }

    // Tooltip lines: time range + non-zero counts.
    const lines: string[] = [
      `${fmtTime(bucket.startMs)} → ${fmtTime(bucket.endMs)}`,
      `${bucket.total} ${t('logs.histogram.total')}`
    ]
    for (const sev of STACK_ORDER) {
      const c = bucket.counts[sev]
      if (c) lines.push(`${t(`filter.severityBucket.${sev}`)}: ${c}`)
    }

    return {
      xPx: idx * (w + columnGap),
      startMs: bucket.startMs,
      endMs: bucket.endMs,
      total: bucket.total,
      slices,
      title: lines.join('\n')
    }
  })
})

// Time-axis tick labels — 5 evenly-spaced labels, less is hard to
// read, more clutters the strip below the chart.
const TICK_COUNT = 5
const ticks = computed(() => {
  const n = props.data.buckets.length
  if (n === 0) return []
  const fromMs = props.data.buckets[0]!.startMs
  const toMs = props.data.buckets[n - 1]!.endMs
  const out: { xPx: number; label: string }[] = []
  for (let i = 0; i < TICK_COUNT; i++) {
    const frac = i / (TICK_COUNT - 1)
    out.push({
      xPx: frac * VIEWBOX_WIDTH,
      label: fmtTime(fromMs + frac * (toMs - fromMs))
    })
  }
  return out
})

const isEmpty = computed(() => maxCount.value === 0)
</script>

<template>
  <div class="vellum-log-histogram">
    <div v-if="isEmpty" class="vellum-log-histogram__empty">
      {{ t('logs.histogram.empty') }}
    </div>
    <template v-else>
      <svg
        class="vellum-log-histogram__svg"
        :viewBox="`0 0 ${VIEWBOX_WIDTH} ${PLOT_HEIGHT}`"
        preserveAspectRatio="none"
        role="img"
        :aria-label="t('logs.histogram.ariaLabel')"
      >
        <g v-for="col in columns" :key="col.xPx">
          <title>{{ col.title }}</title>
          <rect
            v-for="(slice, i) in col.slices"
            :key="i"
            :x="col.xPx"
            :y="slice.yPx"
            :width="columnWidth"
            :height="slice.heightPx"
            :fill="slice.fill"
            shape-rendering="crispEdges"
          />
        </g>
      </svg>

      <!-- Footer row: time ticks + inline legend share the same line so
           the chart stays compact (≈ 60px total) and doesn't compete
           with the data grid for vertical real estate. -->
      <div class="vellum-log-histogram__footer">
        <div class="vellum-log-histogram__axis" aria-hidden="true">
          <span
            v-for="tick in ticks"
            :key="tick.xPx"
            class="vellum-log-histogram__tick"
            :style="{ left: `calc(${(tick.xPx / VIEWBOX_WIDTH) * 100}% )` }"
          >{{ tick.label }}</span>
        </div>
        <div class="vellum-log-histogram__legend">
          <span
            v-for="sev in STACK_ORDER"
            :key="sev"
            class="vellum-log-histogram__legend-item"
          >
            <span
              class="vellum-log-histogram__swatch"
              :style="{ background: SEVERITY_COLOR[sev] }"
            />
            {{ t(`filter.severityBucket.${sev}`) }}
          </span>
          <span v-if="data.truncated" class="vellum-log-histogram__truncated">
            {{ t('logs.histogram.truncated') }}
          </span>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.vellum-log-histogram {
  flex: none;
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 4px 12px 6px;
  border-bottom: 1px solid var(--ui-border);
  background: var(--ui-bg);
}
.vellum-log-histogram__svg {
  width: 100%;
  height: 44px;
  display: block;
}
/* Footer row: ticks left, legend right. Single line keeps the
   chart total height around ~60px including padding. */
.vellum-log-histogram__footer {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.62rem;
  color: var(--ui-text-muted);
}
.vellum-log-histogram__axis {
  position: relative;
  flex: 1;
  height: 10px;
  font-variant-numeric: tabular-nums;
}
.vellum-log-histogram__tick {
  position: absolute;
  transform: translateX(-50%);
  white-space: nowrap;
}
/* Anchor first / last labels to chart edges so they don't spill. */
.vellum-log-histogram__tick:first-child { transform: translateX(0); }
.vellum-log-histogram__tick:last-child  { transform: translateX(-100%); }
.vellum-log-histogram__legend {
  display: flex;
  flex-wrap: nowrap;
  gap: 0.55rem;
  align-items: center;
  flex: none;
}
.vellum-log-histogram__legend-item {
  display: inline-flex;
  align-items: center;
  gap: 0.2rem;
  text-transform: capitalize;
}
.vellum-log-histogram__swatch {
  width: 0.5rem;
  height: 0.5rem;
  border-radius: 1px;
  display: inline-block;
}
.vellum-log-histogram__truncated {
  font-style: italic;
}
.vellum-log-histogram__empty {
  font-size: 0.65rem;
  color: var(--ui-text-muted);
  padding: 0.4rem 0.75rem;
}
</style>
