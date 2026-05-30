<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppResizableSplit from '~/components/overlay/AppResizableSplit.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppErrorState from '~/components/ui/AppErrorState.vue'
import AppSkeleton from '~/components/ui/AppSkeleton.vue'
import SpanTree from './components/SpanTree.vue'
import SpanFlameGraph from './components/SpanFlameGraph.vue'
import SpanDetailPanel from './components/SpanDetailPanel.vue'
import { useTracePage } from './useTracePage'
import { buildSpansExport, downloadOtlpJson } from '~/lib/otlpExport'
import {
  buildClipboardMarkdown,
  buildTraceTree,
  copyToClipboard,
  downloadText
} from '~/lib/textExport'
import type { ActionDescriptor, BreadcrumbItem } from '~/types/toolbar'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const { t, locale } = useI18n()
const route = useRoute()
const { $traceService, $logsService } = useNuxtApp()

// The list page forwards its filter query to the detail URL so the
// "Traces" breadcrumb can land back on the same filtered view. When
// the user reaches the detail directly (deep link, refresh, navigation
// from logs / spans), there's no query to carry — the link falls back
// to a bare `/traces`.
const backToListTo = computed(() => {
  const q = route.query
  return Object.keys(q).length > 0 ? { path: '/traces', query: q } : '/traces'
})

const traceId = computed(() => route.params.traceId as string)
const page = useTracePage($traceService, $logsService, traceId.value)

// Span panel layout — `tree` is the linear list (default, low-density
// scenarios), `flame` is the depth-stacked timeline (better when the
// trace runs deep, e.g. middleware/repository/db chains). Local state
// only — the user picks per-trace; not persisted across reloads.
type SpanView = 'tree' | 'flame'
const spanView = ref<SpanView>('tree')

function fmtTime(iso: string): string {
  return dateTimeFormat(iso, 'datetime-seconds', locale.value)
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
  { labelKey: 'nav.traces', icon: 'i-ph-tree-structure', to: backToListTo.value },
  { label: summary.value?.rootName ?? traceId.value.slice(0, 12) + '…' }
])

function reloadPage() {
  if (import.meta.client) window.location.reload()
}

const subtitle = computed(() => {
  const s = summary.value
  if (!s) return undefined
  return `${fmtTime(s.start)} → ${fmtTime(s.end)} · ${fmtDuration(s.durationMs)} · ${t('traces.detail.spanCount', { count: s.spanCount })}`
})

function exportTraceOtlp() {
  const trace = page.trace.value
  if (!trace) return
  const envelope = buildSpansExport([{ traceId: trace.traceId, spans: trace.spans }])
  downloadOtlpJson(envelope, `trace-${trace.traceId.slice(0, 12)}`)
}
function exportTraceTree() {
  const trace = page.trace.value
  if (!trace) return
  const text = buildTraceTree({ traceId: trace.traceId, spans: trace.spans })
  downloadText(text, `trace-${trace.traceId.slice(0, 12)}`, 'txt')
}

const toast = useToast()
async function copyTraceToClipboard() {
  const trace = page.trace.value
  const s = summary.value
  if (!trace || !s) return
  const rootSpan = trace.spans.find(sp => !sp.parentSpanId) ?? trace.spans[0]
  const context = [
    `Trace: ${trace.traceId}`,
    `Root: ${s.rootName}${rootSpan?.serviceName ? ` · Service: ${rootSpan.serviceName}` : ''}`,
    `Duration: ${fmtDuration(s.durationMs)} · Spans: ${s.spanCount} · Status: ${rootSpan?.statusCode ?? '?'}`,
    `Window: ${s.start} → ${s.end}`
  ]
  const body = buildTraceTree({ traceId: trace.traceId, spans: trace.spans })
  const md = buildClipboardMarkdown('OtlpDashboard trace', context, body)
  const ok = await copyToClipboard(md)
  toast.add(ok
    ? { title: t('common.copied'), color: 'success', icon: 'i-ph-check' }
    : { title: t('common.copyFailed'), color: 'error', icon: 'i-ph-x' })
}

const actions = computed<ActionDescriptor[]>(() => {
  if (!summary.value || !page.trace.value) return []
  const s = summary.value
  return [
    {
      kind: 'split',
      labelKey: 'traces.detail.export.otlp',
      icon: 'i-ph-download-simple',
      onClick: exportTraceOtlp,
      items: [
        { labelKey: 'traces.detail.export.tree', icon: 'i-ph-tree-view', onClick: exportTraceTree },
        { labelKey: 'traces.detail.export.clipboard', icon: 'i-ph-clipboard-text', onClick: copyTraceToClipboard }
      ]
    },
    {
      kind: 'custom',
      labelKey: 'traces.detail.viewLogs',
      icon: 'i-ph-file-text',
      onClick: () => navigateTo({
        path: '/logs',
        query: {
          traceId: page.trace.value!.traceId,
          from: new Date(new Date(s.start).getTime() - 60_000).toISOString(),
          to: new Date(new Date(s.end).getTime() + 60_000).toISOString()
        }
      })
    }
  ]
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
      icon="i-ph-magnifying-glass-minus"
      :title="`Trace ${traceId.slice(0, 12)}…`"
      :description="t('traces.emptyDescription')"
      :retryable="false"
    />

    <AppErrorState
      v-else-if="page.error.value"
      :title="t('traces.errorTitle')"
      :description="page.error.value"
      @retry="reloadPage"
    />

    <template v-else-if="page.trace.value">
      <AppEmptyState
        v-if="page.trace.value.spans.length === 0"
        icon="i-ph-prohibit"
        :title="t('traces.detail.noSpans')"
      />
      <template v-else>
      <UAlert
        v-if="page.trace.value.truncated"
        color="warning"
        variant="subtle"
        icon="i-ph-warning"
        :title="t('traces.detail.truncatedTitle')"
        :description="t('traces.detail.truncatedDescription', { count: page.trace.value.spans.length })"
        class="mb-2"
      />
      <AppResizableSplit
        name="trace-detail"
        :default-ratio="0.62"
      >
        <template #first>
          <div class="h-full flex flex-col border border-default rounded-lg overflow-hidden bg-default">
            <header class="px-3 py-2 border-b border-default flex items-center justify-between gap-3">
              <span class="text-xs uppercase tracking-wide text-muted">
                {{ t('traces.detail.spans') }}
              </span>
              <div class="vellum-span-view-toggle">
                <button
                  type="button"
                  :class="['vellum-span-view-toggle__btn', spanView === 'tree' ? 'vellum-span-view-toggle__btn--active' : '']"
                  @click="spanView = 'tree'"
                >
                  <UIcon name="i-ph-tree-view" class="size-3.5" />
                  <span>{{ t('traces.detail.viewTree') }}</span>
                </button>
                <button
                  type="button"
                  :class="['vellum-span-view-toggle__btn', spanView === 'flame' ? 'vellum-span-view-toggle__btn--active' : '']"
                  @click="spanView = 'flame'"
                >
                  <UIcon name="i-ph-flame" class="size-3.5" />
                  <span>{{ t('traces.detail.viewFlame') }}</span>
                </button>
              </div>
            </header>
            <SpanTree
              v-if="spanView === 'tree'"
              :spans="page.trace.value.spans"
              :logs="page.logs.value"
              :selected-id="page.selected.value?.spanId ?? null"
              @select="(s) => page.selected.value = s"
            />
            <SpanFlameGraph
              v-else
              :spans="page.trace.value.spans"
              :logs="page.logs.value"
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
    </template>
  </AppPage>
</template>

<style scoped>
/* Compact segmented toggle in the spans panel header. Two buttons
   sharing a single rounded border give a clear "this OR that"
   affordance without the visual weight of a full UButton group. */
.vellum-span-view-toggle {
  display: inline-flex;
  border: 1px solid var(--ui-border);
  border-radius: var(--radius-md);
  overflow: hidden;
}
.vellum-span-view-toggle__btn {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  padding: 0.25rem 0.625rem;
  font-size: 11px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--ui-text-muted);
  background: transparent;
  cursor: pointer;
  transition: background-color var(--t-instant) var(--ease-out), color var(--t-instant) var(--ease-out);
}
.vellum-span-view-toggle__btn + .vellum-span-view-toggle__btn {
  border-left: 1px solid var(--ui-border);
}
.vellum-span-view-toggle__btn:hover {
  color: var(--ui-text);
  background: var(--ui-bg-elevated);
}
.vellum-span-view-toggle__btn--active {
  color: var(--ui-text);
  background: var(--ui-bg-elevated);
}
</style>
