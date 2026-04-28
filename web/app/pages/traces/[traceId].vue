<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppResizableSplit from '~/components/overlay/AppResizableSplit.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppErrorState from '~/components/ui/AppErrorState.vue'
import AppSkeleton from '~/components/ui/AppSkeleton.vue'
import SpanTree from './components/SpanTree.vue'
import SpanDetailPanel from './components/SpanDetailPanel.vue'
import { useTracePage } from './useTracePage'
import type { ActionDescriptor, BreadcrumbItem } from '~/types/toolbar'

const { t, locale } = useI18n()
const route = useRoute()
const { $traceService } = useNuxtApp()

const traceId = computed(() => route.params.traceId as string)
const page = useTracePage($traceService, traceId.value)

const formatter = computed(() => new Intl.DateTimeFormat(locale.value, {
  dateStyle: 'short',
  timeStyle: 'medium'
}))
function fmtTime(iso: string): string {
  return formatter.value.format(new Date(iso))
}
function fmtDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

const summary = computed(() => {
  const trace = page.trace.value
  if (!trace || trace.spans.length === 0) return null
  const sorted = [...trace.spans].sort((a, b) => new Date(a.start).getTime() - new Date(b.start).getTime())
  const start = sorted[0]!.start
  const end = sorted.reduce((max, s) => new Date(s.end) > new Date(max) ? s.end : max, sorted[0]!.end)
  const durationMs = new Date(end).getTime() - new Date(start).getTime()
  const root = trace.spans.find(s => !s.parentSpanId) ?? sorted[0]!
  return { start, end, durationMs, spanCount: trace.spans.length, rootName: root.name }
})

const breadcrumb = computed<BreadcrumbItem[]>(() => [
  { labelKey: 'nav.traces', icon: 'i-lucide-waypoints', to: '/traces' },
  { label: summary.value?.rootName ?? traceId.value.slice(0, 12) + '…' }
])

const subtitle = computed(() => {
  const s = summary.value
  if (!s) return undefined
  return `${fmtTime(s.start)} → ${fmtTime(s.end)} · ${fmtDuration(s.durationMs)} · ${t('traces.detail.spanCount', { count: s.spanCount })}`
})

const actions = computed<ActionDescriptor[]>(() => {
  if (!summary.value || !page.trace.value) return []
  const s = summary.value
  return [{
    kind: 'custom',
    labelKey: 'traces.detail.viewLogs',
    icon: 'i-lucide-file-text',
    onClick: () => navigateTo({
      path: '/logs',
      query: {
        traceId: page.trace.value!.traceId,
        from: new Date(new Date(s.start).getTime() - 60_000).toISOString(),
        to: new Date(new Date(s.end).getTime() + 60_000).toISOString()
      }
    })
  }]
})
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :breadcrumb="breadcrumb"
        :title="summary?.rootName ?? traceId"
        :subtitle="subtitle"
        :actions="actions"
      />
    </template>

    <div v-if="page.isLoading.value" class="flex-1 min-h-0 p-2">
      <AppSkeleton :rows="10" row-class="h-9" />
    </div>

    <AppErrorState
      v-else-if="page.notFound.value"
      icon="i-lucide-search-x"
      :title="`Trace ${traceId.slice(0, 12)}…`"
      :description="t('traces.emptyDescription')"
      :retryable="false"
    />

    <AppErrorState
      v-else-if="page.error.value"
      :title="t('traces.errorTitle')"
      :description="page.error.value"
      @retry="() => window.location.reload()"
    />

    <template v-else-if="page.trace.value">
      <AppEmptyState
        v-if="page.trace.value.spans.length === 0"
        icon="i-lucide-circle-slash"
        :title="t('traces.detail.noSpans')"
      />
      <AppResizableSplit
        v-else
        name="trace-detail"
        :default-ratio="0.62"
      >
        <template #first>
          <div class="h-full flex flex-col border border-default rounded-lg overflow-hidden bg-default">
            <header class="px-3 py-2 border-b border-default text-xs uppercase tracking-wide text-muted">
              {{ t('traces.detail.spans') }}
            </header>
            <SpanTree
              :spans="page.trace.value.spans"
              :selected-id="page.selected.value?.spanId ?? null"
              @select="(s) => page.selected.value = s"
            />
          </div>
        </template>
        <template #second>
          <div class="h-full flex flex-col border border-default rounded-lg overflow-hidden bg-default ml-1">
            <header class="px-3 py-2 border-b border-default text-xs uppercase tracking-wide text-muted">
              {{ t('traces.detail.spanDetail') }}
            </header>
            <SpanDetailPanel :span="page.selected.value" />
          </div>
        </template>
      </AppResizableSplit>
    </template>
  </AppPage>
</template>
