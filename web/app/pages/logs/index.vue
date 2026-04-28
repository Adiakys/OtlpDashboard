<script setup lang="ts">
import { useLogsPage } from './usePage'
import LogsToolbar from './components/LogsToolbar.vue'
import LogsTable from './components/LogsTable.vue'
import LogDetailSlideover from './components/LogDetailSlideover.vue'
import type { TimeWindow } from '~/services/types'

const route = useRoute()
const { $logsService } = useNuxtApp()

// Bootstrap from URL query (e.g. "View Logs" from trace detail sets
// ?traceId=...&from=...&to=...). Fall back to composable defaults if absent.
function strFromQuery(key: string): string | undefined {
  const v = route.query[key]
  return typeof v === 'string' && v.length > 0 ? v : undefined
}
const initialTraceId = strFromQuery('traceId')
const from = strFromQuery('from')
const to = strFromQuery('to')
const initialRange: TimeWindow | undefined = from && to ? { from, to } : undefined

const page = useLogsPage($logsService, { initialTraceId, initialRange })
</script>

<template>
  <div class="h-full flex flex-col gap-4">
    <h1 class="text-xl font-semibold">
      Logs
    </h1>

    <LogsToolbar
      v-model:range="page.range.value"
      v-model:limit="page.limit.value"
      v-model:service="page.service.value"
      :trace-id="page.traceId.value"
      :live="page.isLive.value"
      :services="page.availableServices.value"
      @reload="page.reload"
      @toggle-live="page.toggleLive"
    />

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-lucide-alert-triangle"
      :title="page.error.value"
    />

    <LogsTable
      class="flex-1 min-h-0"
      :items="page.items.value"
      :loading="page.isLoading.value"
      :has-more="!page.isLive.value && page.hasMore.value"
      @select="row => page.selected.value = row"
      @load-more="page.loadMore"
    />

    <LogDetailSlideover v-model="page.selected.value" />
  </div>
</template>
