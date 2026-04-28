<script setup lang="ts">
import type { SpanDto } from '~/services/types'
import AppBadge from '~/components/ui/AppBadge.vue'

interface DisplaySpan {
  span: SpanDto
  depth: number
  /** Relative offset of the span start within the trace [0,1]. */
  offset: number
  /** Relative width of the span (duration / trace duration) [0,1]. */
  width: number
}

const props = defineProps<{
  spans: SpanDto[]
  selectedId?: string | null
}>()

defineEmits<{ select: [span: SpanDto] }>()

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

  return [...props.spans]
    .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
    .map(s => {
      const start = new Date(s.start).getTime()
      const end = new Date(s.end).getTime()
      return {
        span: s,
        depth: depthOf(s),
        offset: (start - traceStart) / span,
        width: Math.max(0.005, (end - start) / span)
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
        v-for="{ span, depth, offset, width } in rows"
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
              name="i-lucide-corner-down-right"
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
          <div class="relative h-2 flex-1 rounded-full bg-elevated overflow-hidden">
            <div
              class="absolute inset-y-0 rounded-full transition-[left,width] duration-300"
              :class="span.statusCode === 'Error' ? 'bg-error' : 'bg-primary'"
              :style="{ left: `${offset * 100}%`, width: `${width * 100}%` }"
            />
          </div>
          <span class="font-mono text-xs text-muted shrink-0 w-16 text-right">
            {{ fmtDuration(span.durationMs) }}
          </span>
        </div>
      </li>
    </ul>
  </div>
</template>
