<script setup lang="ts">
import type { LogRecordDto } from '~/services/types'

defineProps<{
  items: LogRecordDto[]
  loading: boolean
  hasMore: boolean
}>()

defineEmits<{
  select: [record: LogRecordDto]
  loadMore: []
}>()

// Severity number buckets follow OTLP spec: 1-4 TRACE, 5-8 DEBUG, 9-12 INFO,
// 13-16 WARN, 17-20 ERROR, 21-24 FATAL.
function severityColor(n: number): 'neutral' | 'info' | 'success' | 'warning' | 'error' {
  if (n >= 21) return 'error'
  if (n >= 17) return 'error'
  if (n >= 13) return 'warning'
  if (n >= 9) return 'success'
  if (n >= 5) return 'info'
  return 'neutral'
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleString()
}

function truncate(s: string | null, max = 240): string {
  if (!s) return ''
  return s.length > max ? `${s.slice(0, max)}…` : s
}
</script>

<template>
  <div class="border border-default rounded overflow-y-auto">
    <table class="w-full text-sm">
      <thead class="bg-elevated text-left sticky top-0 z-10">
        <tr>
          <th class="px-3 py-2 font-medium whitespace-nowrap w-px">
            Time
          </th>
          <th class="px-3 py-2 font-medium whitespace-nowrap w-px">
            Application
          </th>
          <th class="px-3 py-2 font-medium whitespace-nowrap w-px">
            Severity
          </th>
          <th class="px-3 py-2 font-medium w-full">
            Body
          </th>
          <th class="px-3 py-2 font-medium whitespace-nowrap w-px">
            Scope
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="loading && items.length === 0">
          <td colspan="5" class="px-3 py-6 text-center text-muted">
            Loading…
          </td>
        </tr>
        <tr v-else-if="items.length === 0">
          <td colspan="5" class="px-3 py-6 text-center text-muted">
            No logs in this window.
          </td>
        </tr>
        <tr
          v-for="(row, i) in items"
          :key="i"
          class="border-t border-default hover:bg-elevated cursor-pointer"
          @click="$emit('select', row)"
        >
          <td class="px-3 py-2 text-xs font-mono whitespace-nowrap">
            {{ formatTime(row.time) }}
          </td>
          <td
            class="px-3 py-2 text-xs font-mono text-muted whitespace-nowrap truncate max-w-32"
            :title="row.serviceName ?? ''"
          >
            {{ row.serviceName ?? '—' }}
          </td>
          <td class="px-3 py-2 whitespace-nowrap">
            <UBadge :color="severityColor(row.severityNumber)" size="sm" variant="subtle">
              {{ row.severityText ?? row.severityNumber }}
            </UBadge>
          </td>
          <td class="px-3 py-2 text-default w-full">
            {{ truncate(row.body) }}
          </td>
          <td
            class="px-3 py-2 text-xs text-muted whitespace-nowrap truncate max-w-32"
            :title="row.scopeName ?? ''"
          >
            {{ row.scopeName ?? '—' }}
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
