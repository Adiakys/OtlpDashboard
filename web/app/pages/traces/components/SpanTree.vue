<script setup lang="ts">
import type { LogRecordDto, SpanDto } from '~/services/types'
import AppBadge from '~/components/ui/AppBadge.vue'
import { type AlertBucket, buildTraceLayout } from '../composables/useTraceLayout'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const props = defineProps<{
  spans: SpanDto[]
  /** Correlated logs for the whole trace. Filtered + grouped by spanId
   *  inside <c>useTraceLayout</c> so the parent doesn't need to know
   *  about severity buckets. */
  logs?: LogRecordDto[]
  selectedId?: string | null
}>()

defineEmits<{ select: [span: SpanDto] }>()

const { locale } = useI18n()
function fmtAlertTime(iso: string): string {
  return dateTimeFormat(iso, 'time-ms', locale.value)
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

const rows = computed(() => buildTraceLayout(props.spans, props.logs).spans)

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
