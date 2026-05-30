<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import TraceStatusBadgeCell from '~/components/data/cells/TraceStatusBadgeCell.vue'
import TraceServiceCell from '~/components/data/cells/TraceServiceCell.vue'
import DurationBarCell from '~/components/data/cells/DurationBarCell.vue'
import { useTracesPage } from './usePage'
import { buildSpansExport, downloadOtlpJson, type TraceSpans } from '~/lib/otlpExport'
import {
  buildClipboardMarkdown,
  buildTraceTrees,
  buildTracesCsv,
  buildTracesSummaryList,
  copyToClipboard,
  downloadText
} from '~/lib/textExport'
import type {
  ActionDescriptor,
  FilterDescriptor
} from '~/types/toolbar'
import type { TimeWindow, TraceSummaryDto } from '~/services/types'
import type { TraceStatusFilter } from '~/types/filters'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const { $traceService, $traceRetentionDays, $queryMaxWindowHours } = useNuxtApp()

// Hydrate filter state from the URL — bookmarks / shared links / hard
// refreshes all land on the same view. The composable's `queryState`
// computed below tracks the live state in URL form, watched here.
function strFromQuery(key: string): string | undefined {
  const v = route.query[key]
  return typeof v === 'string' && v.length > 0 ? v : undefined
}
function strArrayFromQuery(key: string): string[] {
  const v = route.query[key]
  if (Array.isArray(v)) return v.filter((x): x is string => typeof x === 'string' && x.length > 0)
  if (typeof v === 'string' && v.length > 0) return [v]
  return []
}
function numFromQuery(key: string): number | undefined {
  const s = strFromQuery(key)
  if (!s) return undefined
  const n = Number(s)
  return Number.isFinite(n) && n > 0 ? n : undefined
}
// `?range=1h` takes precedence over `from`/`to` when present: the URL
// is encoding a rolling-window intent, and the composable will
// recompute the absolute window from `now` on hydration.
const presetQ = strFromQuery('range')
const fromQ = strFromQuery('from')
const toQ = strFromQuery('to')
const initialRange: TimeWindow | undefined = fromQ && toQ ? { from: fromQ, to: toQ } : undefined
const statusQ = strFromQuery('status')
const initialStatus: TraceStatusFilter | undefined =
  statusQ === 'ok' || statusQ === 'error' ? statusQ : undefined
const minMsQ = numFromQuery('minMs')
const maxMsQ = numFromQuery('maxMs')
const initialDuration = (minMsQ != null || maxMsQ != null)
  ? { minMs: minMsQ ?? null, maxMs: maxMsQ ?? null }
  : undefined

// `services=A,B,C` is the modern shape; we also accept the legacy
// `service=foo` so deep-links shared from older builds still resolve
// to the same filtered view.
const initialServices = strArrayFromQuery('services').flatMap(s => s.split(',').map(t => t.trim()).filter(Boolean))
const legacyService = strFromQuery('service')
if (legacyService) initialServices.push(legacyService)

const page = useTracesPage($traceService, {
  initialRange,
  initialPreset: presetQ,
  initialServices,
  initialNoService: strFromQuery('noService') === 'true',
  initialServiceMatch: strFromQuery('serviceMatch') === 'any' ? 'any' : 'root',
  initialStatus,
  initialDuration,
  initialSearch: strFromQuery('spanNameContains'),
  initialAttr: strArrayFromQuery('attr'),
  initialLimit: numFromQuery('limit'),
  // Auto-start live unless the URL says otherwise — that's how
  // navigating to /traces/{id} and back preserves the user's choice
  // to disable it.
  initialLive: strFromQuery('live') !== 'false'
})

// Persist filter changes to the URL via `replace` — back button stays
// useful (no history flood for keystrokes); composable strips defaults
// so the URL is compact.
watch(page.queryState, (q) => {
  void router.replace({ query: q })
}, { deep: true })

const maxDuration = computed(() => page.items.value.reduce((m, r) => Math.max(m, r.durationMs), 1))

const subtitle = computed(() => t('traces.subtitle', {
  count: page.items.value.length,
  window: describeWindow(page.range.value)
}))

function describeWindow(range: TimeWindow): string {
  const f = new Date(range.from)
  const tt = new Date(range.to)
  const fmt = new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' })
  return `${fmt.format(f)} → ${fmt.format(tt)}`
}

const filters: FilterDescriptor[] = [
  // Application stays interactive in live mode: changing it triggers a reload
  // (watcher inside useTracesPage) and the next live tick uses the new filter.
  { kind: 'application', modelValue: page.service, options: page.availableServices, matchMode: page.serviceMatch },
  { kind: 'time-range', modelValue: page.range, preset: page.rangePreset, disabled: page.isLive, retentionDays: $traceRetentionDays, maxWindowHours: $queryMaxWindowHours },
  { kind: 'status', modelValue: page.statusFilter },
  { kind: 'duration', modelValue: page.durationFilter },
  { kind: 'attributes', modelValue: page.attributeFilters },
  { kind: 'limit', modelValue: page.limit, disabled: page.isLive }
]

// Trace summaries only carry the root span info; the OTLP envelope needs
// every span, so we fan out detail fetches with a small concurrency cap
// (good citizenship toward the API and the browser's connection pool).
// Failures on individual traces are swallowed and skipped — the export is
// best-effort, the same way the metrics-tree export is.
const EXPORT_CONCURRENCY = 6
const isExporting = ref(false)
const exportDisabled = computed(() => page.items.value.length === 0 || isExporting.value)

async function fetchTraceSpansBounded(ids: string[]): Promise<TraceSpans[]> {
  const out: TraceSpans[] = []
  let cursor = 0
  async function worker() {
    while (cursor < ids.length) {
      const idx = cursor++
      const id = ids[idx]!
      try {
        const detail = await $traceService.getTrace(id)
        out.push({ traceId: detail.traceId, spans: detail.spans })
      } catch {
        // Drop the trace from the export rather than aborting — matches the
        // "best-effort" behaviour the rest of the page already follows for
        // transient backend errors.
      }
    }
  }
  await Promise.all(Array.from({ length: Math.min(EXPORT_CONCURRENCY, ids.length) }, worker))
  return out
}

// Both export formats share the same fetch: the only difference is the
// serialiser at the end. Keeping the fetch in a thunk means switching
// formats from the dropdown doesn't re-issue every detail call.
async function exportTracesWith(serialise: (traces: TraceSpans[]) => void) {
  if (page.items.value.length === 0 || isExporting.value) return
  isExporting.value = true
  try {
    const ids = page.items.value.map(r => r.traceId)
    const traces = await fetchTraceSpansBounded(ids)
    if (traces.length === 0) return
    serialise(traces)
  } finally {
    isExporting.value = false
  }
}

function exportTracesOtlp() {
  return exportTracesWith(traces => downloadOtlpJson(buildSpansExport(traces), 'traces'))
}
function exportTracesTree() {
  return exportTracesWith(traces => downloadText(buildTraceTrees(traces), 'traces', 'txt'))
}
// CSV is the on-screen grid: only the summaries are needed, no per-trace
// detail fetch — same `page.items` the table already renders.
function exportTracesCsv() {
  if (page.items.value.length === 0) return
  downloadText(buildTracesCsv(page.items.value), 'traces', 'csv')
}

const toast = useToast()
async function copyTracesToClipboard() {
  if (page.items.value.length === 0) return
  const filters: string[] = []
  if (page.service.value.length > 0) filters.push(`service=${page.service.value.join(',')}`)
  if (page.statusFilter.value !== 'any') filters.push(`status=${page.statusFilter.value}`)
  const d = page.durationFilter.value
  if (d.minMs != null) filters.push(`duration_ms>=${d.minMs}`)
  if (d.maxMs != null) filters.push(`duration_ms<=${d.maxMs}`)
  if (page.searchQuery.value.trim()) filters.push(`span_name~="${page.searchQuery.value.trim()}"`)
  if (page.attributeFilters.value.length > 0) filters.push(`attr=${page.attributeFilters.value.join(',')}`)
  const context = [
    `Window: ${page.range.value.from} → ${page.range.value.to}`,
    `Filters: ${filters.length > 0 ? filters.join(' · ') : '(none)'}`,
    `Count: ${page.items.value.length} traces`
  ]
  const md = buildClipboardMarkdown('OtlpDashboard traces', context, buildTracesSummaryList(page.items.value))
  const ok = await copyToClipboard(md)
  toast.add(ok
    ? { title: t('common.copied'), color: 'success', icon: 'i-ph-check' }
    : { title: t('common.copyFailed'), color: 'error', icon: 'i-ph-x' })
}

const actions: ActionDescriptor[] = [
  {
    kind: 'split',
    labelKey: 'traces.export.otlp',
    icon: 'i-ph-download-simple',
    onClick: exportTracesOtlp,
    loading: isExporting,
    disabled: exportDisabled,
    items: [
      { labelKey: 'traces.export.tree', icon: 'i-ph-tree-view', onClick: exportTracesTree },
      { labelKey: 'traces.export.csv', icon: 'i-ph-file-csv', onClick: exportTracesCsv },
      { labelKey: 'traces.export.clipboard', icon: 'i-ph-clipboard-text', onClick: copyTracesToClipboard }
    ]
  },
  { kind: 'refresh', loading: page.isLoading, disabled: page.isLive, onClick: page.reload },
  { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive }
]

// ISO-ish `yyyy-MM-dd HH:mm:ss`. Locale-independent so the monospace
// column stays aligned across multi-day windows. The trace list doesn't
// need millisecond precision (durationMs is a separate column).
function formatTimestamp(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} `
    + `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

// Column sizing — paired with AppDataGrid's `autoSizeStrategy: fitGridWidth`.
// `width` here is the proportional target the grid scales up so columns
// exactly meet the right edge on first paint. `minWidth` floors each column
// at its smallest legible state — narrow viewports get horizontal scroll
// instead of unreadable squashed cells.
const columnDefs = computed<ColDef<TraceSummaryDto>[]>(() => [
  {
    field: 'start',
    headerName: t('traces.col.start'),
    width: 170,
    minWidth: 160,
    sort: 'desc',
    cellClass: 'vellum-cell-mono',
    valueFormatter: p => p.value ? formatTimestamp(p.value as string) : ''
  },
  {
    field: 'serviceName',
    headerName: t('traces.col.service'),
    width: 150,
    minWidth: 110,
    // Custom renderer surfaces the "(+N other services)" badge when
    // the trace is distributed; falls back to the bare name (or `·`)
    // when it stays inside one service.
    cellRenderer: TraceServiceCell
  },
  {
    field: 'rootSpanName',
    headerName: t('traces.col.rootSpan'),
    width: 320,
    minWidth: 160
  },
  {
    field: 'durationMs',
    headerName: t('traces.col.duration'),
    width: 170,
    minWidth: 130,
    cellRenderer: DurationBarCell,
    cellRendererParams: { max: () => maxDuration.value }
  },
  {
    field: 'spanCount',
    headerName: t('traces.col.spans'),
    width: 72,
    minWidth: 60,
    cellClass: 'vellum-cell-mono vellum-cell-num',
    type: 'rightAligned'
  },
  {
    field: 'rootStatusCode',
    headerName: t('traces.col.status'),
    width: 88,
    minWidth: 80,
    cellRenderer: TraceStatusBadgeCell
  },
  {
    field: 'traceId',
    headerName: t('traces.col.traceId'),
    width: 130,
    minWidth: 110,
    cellClass: 'vellum-cell-mono vellum-cell-muted',
    valueFormatter: p => (p.value as string).slice(0, 16) + '…',
    tooltipField: 'traceId'
  }
])

function onRowClick(row: TraceSummaryDto) {
  // Carry the current filter query through to the detail route so the
  // breadcrumb's "Traces" back-link can restore exactly the view the
  // user came from. The detail page ignores these params; they ride
  // along purely as breadcrumb state.
  navigateTo({ path: `/traces/${row.traceId}`, query: route.query })
}
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :title="t('traces.title')"
        :subtitle="subtitle"
        :filters="filters"
        :actions="actions"
      >
        <template #filters-extra>
          <AppSearchInput v-model="page.searchQuery.value" :placeholder="t('filter.searchTrace')" />
        </template>
      </AppToolbar>
    </template>

    <AppDataGrid
      :column-defs="columnDefs"
      :row-data="page.items.value"
      :loading="page.isLoading.value"
      :error="page.error.value"
      :get-row-id="(r: TraceSummaryDto) => r.traceId"
      :empty-title="t('traces.emptyTitle')"
      :empty-description="t('traces.emptyDescription')"
      :error-title="t('traces.errorTitle')"
      :row-height="40"
      :live="page.isLive.value"
      :has-more="!page.isLive.value && page.hasMore.value"
      :loading-more="page.isLoading.value && page.items.value.length > 0"
      @row-click="onRowClick"
      @retry="page.reload"
      @load-more="page.loadMore"
    />
  </AppPage>
</template>
