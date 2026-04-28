<script setup lang="ts">
import type { TraceSummaryDto } from '~/services/types'

defineProps<{
  items: TraceSummaryDto[]
  loading: boolean
  hasMore: boolean
}>()

defineEmits<{ loadMore: [] }>()

function statusColor(code: string): 'neutral' | 'success' | 'error' {
  if (code === 'Ok') return 'success'
  if (code === 'Error') return 'error'
  return 'neutral'
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleString()
}

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}
</script>

<template>
  <div class="border border-default rounded overflow-y-auto">
    <table class="w-full text-sm">
      <thead class="bg-elevated text-left sticky top-0 z-10">
        <tr>
          <th class="px-3 py-2 font-medium">
            Start
          </th>
          <th class="px-3 py-2 font-medium">
            Application
          </th>
          <th class="px-3 py-2 font-medium">
            Root span
          </th>
          <th class="px-3 py-2 font-medium">
            Duration
          </th>
          <th class="px-3 py-2 font-medium">
            Spans
          </th>
          <th class="px-3 py-2 font-medium">
            Status
          </th>
          <th class="px-3 py-2 font-medium">
            Trace ID
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="loading && items.length === 0">
          <td colspan="7" class="px-3 py-6 text-center text-muted">
            Loading…
          </td>
        </tr>
        <tr v-else-if="items.length === 0">
          <td colspan="7" class="px-3 py-6 text-center text-muted">
            No traces in this window.
          </td>
        </tr>
        <tr
          v-for="row in items"
          :key="row.traceId"
          class="border-t border-default hover:bg-elevated cursor-pointer"
          @click="$router.push(`/traces/${row.traceId}`)"
        >
          <td class="px-3 py-2 text-xs font-mono whitespace-nowrap">
            {{ formatTime(row.start) }}
          </td>
          <td class="px-3 py-2 text-xs font-mono text-muted truncate max-w-xs">
            {{ row.serviceName ?? '—' }}
          </td>
          <td class="px-3 py-2">
            {{ row.rootSpanName }}
          </td>
          <td class="px-3 py-2 font-mono text-xs">
            {{ formatDuration(row.durationMs) }}
          </td>
          <td class="px-3 py-2 text-xs">
            {{ row.spanCount }}
          </td>
          <td class="px-3 py-2">
            <UBadge :color="statusColor(row.rootStatusCode)" size="sm" variant="subtle">
              {{ row.rootStatusCode }}
            </UBadge>
          </td>
          <td class="px-3 py-2 font-mono text-xs text-muted truncate max-w-xs">
            {{ row.traceId }}
          </td>
        </tr>
      </tbody>
    </table>

    <div
      v-if="items.length > 0"
      class="py-2 flex justify-center border-t border-default"
    >
      <LoadMoreButton :has-more="hasMore" :loading="loading" @click="$emit('loadMore')" />
    </div>
  </div>
</template>
