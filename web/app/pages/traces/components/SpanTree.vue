<script setup lang="ts">
import type { LogRecordDto, SpanDto } from '~/services/types'
import { severityBucketFromNumber, type SeverityBucket } from '~/types/filters'
import AppBadge from '~/components/ui/AppBadge.vue'

type AlertBucket = Extract<SeverityBucket, 'warn' | 'error' | 'fatal'>

interface LogMarker {
  /** Position [0,1] within the trace timeline (same coordinate system
   *  as the bar's `offset`). */
  position: number
  bucket: AlertBucket
  body: string
  time: string
  /** Stable key for the v-for. */
  key: string
}

interface DisplaySpan {
  span: SpanDto
  depth: number
  /** Relative offset of the span start within the trace [0,1]. */
  offset: number
  /** Relative width of the span (duration / trace duration) [0,1]. */
  width: number
  /** Warn/Error/Fatal logs attached to this span, positioned along the
   *  trace timeline. Empty for spans without correlated alerts. */
  alerts: LogMarker[]
}

const props = defineProps<{
  spans: SpanDto[]
  /** Correlated logs for the whole trace. Filtered + grouped by spanId
   *  here so the parent doesn't need to know about severity buckets. */
  logs?: LogRecordDto[]
  selectedId?: string | null
}>()

defineEmits<{ select: [span: SpanDto] }>()

const { locale } = useI18n()
const tooltipFormatter = computed(() => new Intl.DateTimeFormat(locale.value, {
  hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3
}))
function fmtAlertTime(iso: string): string {
  return tooltipFormatter.value.format(new Date(iso))
}

function alertIcon(bucket: AlertBucket): string {
  // Filled variants — outlines are too thin to read at the marker size,
  // especially when sitting on top of the colored duration bar. Triangle
  // for "heads up" (warn), octagon for "stop" (error/fatal).
  return bucket === 'warn' ? 'i-ph-warning-fill' : 'i-ph-warning-octagon-fill'
}

function alertColorClass(bucket: AlertBucket): string {
  return bucket === 'warn' ? 'text-warning' : 'text-error'
}

// Hairline ring matching the bucket's accent. Reduced opacity keeps it
// "barely there" — the badge still reads as a neutral chip with a tiny
// coloured halo, not a coloured outline that competes with the icon.
function alertRingClass(bucket: AlertBucket): string {
  return bucket === 'warn' ? 'ring-warning/60' : 'ring-error/60'
}

const rows = computed<DisplaySpan[]>(() => {
  if (props.spans.length === 0) return []
  const byId = new Map<string, SpanDto>()
  for (const s of props.spans) byId.set(s.spanId, s)

  const traceStart = props.spans.reduce((min, s) => {
    const t = new Date(s.start).getTime()
    return t < min ? t : min
  }, Number.POSITIVE_INFINITY)
  const traceEnd = props.spans.reduce((max, s) => {
    const t = new Date(s.end).getTime()
    return t > max ? t : max
  }, Number.NEGATIVE_INFINITY)
  const span = Math.max(1, traceEnd - traceStart)

  const depthCache = new Map<string, number>()
  function depthOf(s: SpanDto, guard = 0): number {
    if (guard > 64) return 0
    const cached = depthCache.get(s.spanId)
    if (cached !== undefined) return cached
    if (!s.parentSpanId) {
      depthCache.set(s.spanId, 0)
      return 0
    }
    const parent = byId.get(s.parentSpanId)
    const d = parent ? depthOf(parent, guard + 1) + 1 : 0
    depthCache.set(s.spanId, d)
    return d
  }

  // Bucket the alert-level logs by their owning spanId once, so each
  // row's lookup is O(1) instead of O(N) over the full log set.
  const alertsBySpanId = new Map<string, LogMarker[]>()
  for (const log of props.logs ?? []) {
    if (!log.spanId) continue
    const bucket = severityBucketFromNumber(log.severityNumber)
    if (bucket !== 'warn' && bucket !== 'error' && bucket !== 'fatal') continue
    const arr = alertsBySpanId.get(log.spanId) ?? []
    arr.push({
      position: ((new Date(log.time).getTime()) - traceStart) / span,
      bucket,
      body: log.body ?? '',
      time: log.time,
      key: `${log.spanId}|${log.time}|${(log.body ?? '').slice(0, 32)}`
    })
    alertsBySpanId.set(log.spanId, arr)
  }

  return [...props.spans]
    .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
    .map(s => {
      const start = new Date(s.start).getTime()
      const end = new Date(s.end).getTime()
      return {
        span: s,
        depth: depthOf(s),
        offset: (start - traceStart) / span,
        width: Math.max(0.005, (end - start) / span),
        alerts: alertsBySpanId.get(s.spanId) ?? []
      }
    })
})

function fmtDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}
</script>

<template>
  <div class="flex-1 min-h-0 overflow-y-auto">
    <ul class="divide-y divide-default">
      <li
        v-for="{ span, depth, offset, width, alerts } in rows"
        :key="span.spanId"
        class="grid grid-cols-[minmax(0,1fr)_minmax(180px,2fr)] gap-3 px-3 py-2 hover:bg-elevated cursor-pointer transition-colors"
        :class="[
          selectedId === span.spanId ? 'bg-elevated' : '',
          span.statusCode === 'Error' ? 'border-l-2 border-error pl-2' : ''
        ]"
        @click="$emit('select', span)"
      >
        <div class="min-w-0 flex items-center gap-2">
          <span :style="{ paddingLeft: `${depth * 0.9}rem` }" class="inline-flex items-center gap-1.5 min-w-0">
            <UIcon
              v-if="depth > 0"
              name="i-ph-arrow-elbow-down-right"
              class="size-3 text-muted shrink-0"
            />
            <span class="text-sm truncate" :title="span.name">{{ span.name }}</span>
          </span>
          <AppBadge
            :tone="{ kind: 'trace-status', status: span.statusCode }"
            size="xs"
            class="shrink-0"
          >
            {{ span.statusCode }}
          </AppBadge>
        </div>

        <div class="flex items-center gap-2">
          <!-- Bar lane: a relative parent that owns BOTH the clipped duration
               track and the alert-marker overlay. Markers share the same
               %-coordinate system as the bar's `left/width`, so they line up
               naturally. The lane is taller than the track itself so the
               14px icons fit without negative offsets — the track stays h-2
               via its own size, just centred via flex on the parent. -->
          <div class="relative flex-1 h-4 flex items-center">
            <div class="w-full h-2 rounded-full bg-elevated overflow-hidden relative">
              <div
                class="absolute inset-y-0 rounded-full transition-[left,width] duration-300"
                :class="span.statusCode === 'Error' ? 'bg-error' : 'bg-primary'"
                :style="{ left: `${offset * 100}%`, width: `${width * 100}%` }"
              />
            </div>
            <UTooltip
              v-for="m in alerts"
              :key="m.key"
              :ui="{ content: 'h-auto !items-start py-1.5 max-w-md' }"
            >
              <!-- Marker: a 20px circular badge with a contrasting bg so
                   the filled warn/error glyph reads cleanly even when
                   sitting on top of a same-coloured (red) error bar.
                   The shadow gives it a slight lift off the track and
                   the ring stops it from blending into either the page
                   bg or an `bg-elevated` row hover. -->
              <span
                class="absolute z-10 inline-flex size-5 items-center justify-center rounded-full bg-default ring-1 shadow-sm cursor-help"
                :class="alertRingClass(m.bucket)"
                :style="{
                  left: `calc(${m.position * 100}% - 10px)`,
                  top: '50%',
                  transform: 'translateY(-50%)'
                }"
                @click.stop
              >
                <UIcon
                  :name="alertIcon(m.bucket)"
                  class="size-3.5"
                  :class="alertColorClass(m.bucket)"
                />
              </span>
              <template #content>
                <div class="flex flex-col gap-1 text-xs leading-snug">
                  <div class="font-mono text-muted">{{ fmtAlertTime(m.time) }}</div>
                  <div class="whitespace-pre-wrap break-words">{{ m.body || '—' }}</div>
                </div>
              </template>
            </UTooltip>
          </div>
          <span class="font-mono text-xs text-muted shrink-0 w-16 text-right">
            {{ fmtDuration(span.durationMs) }}
          </span>
        </div>
      </li>
    </ul>
  </div>
</template>
