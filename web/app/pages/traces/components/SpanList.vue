<script setup lang="ts">
import type { SpanDto } from '~/services/types'

const props = defineProps<{ spans: SpanDto[] }>()
defineEmits<{ select: [span: SpanDto] }>()

// Build a simple parent→children map for indent. Fall back to flat order if
// the parent chain can't be resolved (e.g. orphan spans).
interface DisplaySpan {
  span: SpanDto
  depth: number
}

const rows = computed<DisplaySpan[]>(() => {
  const byId = new Map<string, SpanDto>()
  for (const s of props.spans) byId.set(s.spanId, s)

  const depthCache = new Map<string, number>()
  function depthOf(span: SpanDto, guard = 0): number {
    if (guard > 64) return 0 // cycle protection
    const cached = depthCache.get(span.spanId)
    if (cached !== undefined) return cached
    if (!span.parentSpanId) {
      depthCache.set(span.spanId, 0)
      return 0
    }
    const parent = byId.get(span.parentSpanId)
    const d = parent ? depthOf(parent, guard + 1) + 1 : 0
    depthCache.set(span.spanId, d)
    return d
  }

  return [...props.spans]
    .sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
    .map(span => ({ span, depth: depthOf(span) }))
})

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

function statusColor(code: string): 'neutral' | 'success' | 'error' {
  if (code === 'Ok') return 'success'
  if (code === 'Error') return 'error'
  return 'neutral'
}
</script>

<template>
  <div class="border border-default rounded overflow-y-auto">
    <table class="w-full text-sm">
      <thead class="bg-elevated text-left sticky top-0 z-10">
        <tr>
          <th class="px-3 py-2 font-medium">
            Span
          </th>
          <th class="px-3 py-2 font-medium">
            Application
          </th>
          <th class="px-3 py-2 font-medium">
            Kind
          </th>
          <th class="px-3 py-2 font-medium">
            Duration
          </th>
          <th class="px-3 py-2 font-medium">
            Status
          </th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="{ span, depth } in rows"
          :key="span.spanId"
          class="border-t border-default hover:bg-elevated cursor-pointer"
          @click="$emit('select', span)"
        >
          <td class="px-3 py-2">
            <span :style="{ paddingLeft: `${depth * 1.2}rem` }" class="inline-block">
              <UIcon
                v-if="depth > 0"
                name="i-lucide-corner-down-right"
                class="size-3 text-muted align-text-bottom mr-1"
              />
              {{ span.name }}
            </span>
          </td>
          <td class="px-3 py-2 text-xs font-mono text-muted truncate max-w-xs">
            {{ span.serviceName ?? '—' }}
          </td>
          <td class="px-3 py-2 text-xs text-muted">
            {{ span.kind }}
          </td>
          <td class="px-3 py-2 font-mono text-xs">
            {{ formatDuration(span.durationMs) }}
          </td>
          <td class="px-3 py-2">
            <UBadge :color="statusColor(span.statusCode)" size="sm" variant="subtle">
              {{ span.statusCode }}
            </UBadge>
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
