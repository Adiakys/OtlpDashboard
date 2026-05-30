<script setup lang="ts">
/**
 * Icicle-style flame chart of a trace's spans. Each span occupies a
 * lane indexed by its tree depth (root → 0); horizontal placement is
 * the same `offset/width` `SpanTree` uses, so the two views stay
 * pixel-coherent for the same trace.
 *
 * The rendering shape (overlapping lanes vs. a linear list) is the
 * only thing that differs from `SpanTree`. The shared layout helper
 * <c>buildTraceLayout</c> keeps the depth / alert / position math in
 * one place — fix it once, both views inherit.
 */
import type { LogRecordDto, SpanDto } from '~/services/types'
import { type AlertBucket, buildTraceLayout } from '../composables/useTraceLayout'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const props = defineProps<{
  spans: SpanDto[]
  logs?: LogRecordDto[]
  selectedId?: string | null
}>()

defineEmits<{ select: [span: SpanDto] }>()

const { locale } = useI18n()

const layout = computed(() => buildTraceLayout(props.spans, props.logs))

// Lane geometry. The numbers are tuned for legibility at the default
// trace-detail panel width: 24 px gives room for a one-line span name
// plus a duration tail on the right when there's space; 2 px between
// lanes keeps the icicle structure readable without wasting vertical
// estate on deep traces.
const LANE_HEIGHT = 24
const LANE_GAP = 2

const totalHeightPx = computed(() =>
  (layout.value.maxDepth + 1) * (LANE_HEIGHT + LANE_GAP) + LANE_GAP
)

function alertIcon(bucket: AlertBucket): string {
  return bucket === 'warn' ? 'i-ph-warning-fill' : 'i-ph-warning-octagon-fill'
}
function alertColor(bucket: AlertBucket): string {
  return bucket === 'warn' ? 'text-warning' : 'text-error'
}
function alertRing(bucket: AlertBucket): string {
  return bucket === 'warn' ? 'ring-warning/60' : 'ring-error/60'
}

function fmtDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

function fmtAlertTime(iso: string): string {
  return dateTimeFormat(iso, 'time-ms', locale.value)
}

/** Convert a trace-relative position to a span-relative position so the
 *  alert badge sits on the right pixel within the absolutely-positioned
 *  span box. Clamped to [0,1] to keep clock-skewed events visually
 *  attached to the span instead of overflowing the box. */
function alertLocalPercent(spanOffset: number, spanWidth: number, alertPosition: number): number {
  if (spanWidth <= 0) return 0
  const local = (alertPosition - spanOffset) / spanWidth
  return Math.max(0, Math.min(1, local)) * 100
}
</script>

<template>
  <div class="flex-1 min-h-0 overflow-auto">
    <div
      class="relative"
      :style="{ height: `${totalHeightPx}px` }"
    >
      <button
        v-for="row in layout.spans"
        :key="row.span.spanId"
        type="button"
        class="absolute flex items-center text-left transition-colors rounded-sm border focus:outline-none cursor-pointer"
        :class="[
          row.span.statusCode === 'Error'
            ? 'bg-error/15 border-error/40 hover:bg-error/25'
            : 'bg-primary/10 border-primary/30 hover:bg-primary/20',
          selectedId === row.span.spanId ? 'ring-2 ring-primary' : ''
        ]"
        :style="{
          top: `${row.depth * (LANE_HEIGHT + LANE_GAP) + LANE_GAP}px`,
          left: `${row.offset * 100}%`,
          width: `max(${row.width * 100}%, 4px)`,
          height: `${LANE_HEIGHT}px`
        }"
        :title="`${row.span.name} · ${fmtDuration(row.span.durationMs)}`"
        @click="$emit('select', row.span)"
      >
        <span class="px-1.5 text-xs truncate flex-1 min-w-0">{{ row.span.name }}</span>
        <span
          v-if="row.width > 0.06"
          class="px-1.5 text-[10px] font-mono opacity-60 shrink-0"
        >
          {{ fmtDuration(row.span.durationMs) }}
        </span>

        <!-- Alert markers — same component as SpanTree; positioned
             relative to the span box (alertLocalPercent translates from
             trace-relative to span-relative). Click stops propagation
             so opening the tooltip doesn't reselect the span. -->
        <UTooltip
          v-for="m in row.alerts"
          :key="m.key"
          :ui="{ content: 'h-auto !items-start py-1.5 max-w-md' }"
        >
          <span
            class="absolute z-10 inline-flex size-5 items-center justify-center rounded-full bg-default ring-1 shadow-sm cursor-help"
            :class="alertRing(m.bucket)"
            :style="{
              left: `calc(${alertLocalPercent(row.offset, row.width, m.position)}% - 10px)`,
              top: '50%',
              transform: 'translateY(-50%)'
            }"
            @click.stop
          >
            <UIcon :name="alertIcon(m.bucket)" class="size-3.5" :class="alertColor(m.bucket)" />
          </span>
          <template #content>
            <div class="flex flex-col gap-1 text-xs leading-snug">
              <div class="font-mono text-muted">{{ fmtAlertTime(m.time) }}</div>
              <div class="whitespace-pre-wrap break-words">{{ m.body || '—' }}</div>
            </div>
          </template>
        </UTooltip>
      </button>
    </div>
  </div>
</template>
