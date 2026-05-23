<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import AppDrawer from '~/components/overlay/AppDrawer.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import SeverityBadgeCell from '~/components/data/cells/SeverityBadgeCell.vue'
import TraceLinkCell from '~/components/data/cells/TraceLinkCell.vue'
import LogDetailContent from './components/LogDetailContent.vue'
import LogsSeverityHistogram from './components/LogsSeverityHistogram.vue'
import { useInMemoryLogsHistogram } from './composables/useSeverityHistogram'
import { useLogsPage } from './usePage'
import { buildLogsExport, downloadOtlpJson } from '~/lib/otlpExport'
import type {
  ActionDescriptor,
  FilterDescriptor
} from '~/types/toolbar'
import type { LogRecordDto, TimeWindow } from '~/services/types'
import { SEVERITY_BUCKETS, type SeverityBucket } from '~/types/filters'

const { t, locale } = useI18n()
const route = useRoute()
const router = useRouter()
const { $logsService, $logRetentionDays, $queryMaxWindowHours } = useNuxtApp()

// All filter state seeds from the URL. The composable's `queryState`
// computed below tracks the live state in URL form; the watcher
// below pushes it back via `router.replace` so refreshes / bookmarks
// / shared links all land on the same view.
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
const fromQ = strFromQuery('from')
const toQ = strFromQuery('to')
const initialRange: TimeWindow | undefined = fromQ && toQ ? { from: fromQ, to: toQ } : undefined
const severitiesQ = strFromQuery('severities')
const initialSeverity = severitiesQ
  ? severitiesQ.split(',')
      .map(s => s.trim())
      .filter((s): s is SeverityBucket => SEVERITY_BUCKETS.includes(s as SeverityBucket))
  : undefined

// `services=A,B,C` is the modern shape; legacy `service=foo` URLs
// from older deep-links still merge into the same allow-list.
const initialServices = strArrayFromQuery('services').flatMap(s => s.split(',').map(t => t.trim()).filter(Boolean))
const legacyService = strFromQuery('service')
if (legacyService) initialServices.push(legacyService)

const page = useLogsPage($logsService, {
  initialTraceId: strFromQuery('traceId'),
  initialRange,
  initialServices,
  initialSeverity,
  initialBody: strFromQuery('bodyContains'),
  initialAttr: strArrayFromQuery('attr'),
  initialLimit: numFromQuery('limit'),
  initialLive: strFromQuery('live') !== 'false'
})

// Persist filter state back to the URL. `replace` keeps the back
// button useful (no flood of intermediate states for, e.g., every
// keystroke in the search box). The composable's `queryState`
// already strips defaulted values so the URL stays compact.
watch(page.queryState, (q) => {
  void router.replace({ query: q })
}, { deep: true })

// Severity histogram — frontend-only for now: bucketed from whatever
// the page already loaded. The composable exposes
// `SeverityHistogramData`; swapping in a server-side aggregation
// later is a one-line change at this call site, no renderer work.
// `truncated` flips when the page hit its limit and the server says
// there's more — the histogram surfaces that as a footnote.
const histogram = useInMemoryLogsHistogram(
  computed(() => page.items.value),
  page.range,
  computed(() => page.items.value.length > 0 && page.hasMore.value)
)

// Hide-histogram preference. Persisted in localStorage so refreshes
// keep the user's view choice — same pattern as the sidebar
// collapsed flag. Read once at setup, written on each toggle.
const HIST_STORAGE_KEY = 'logs.histogramHidden'
const histogramHidden = ref(
  import.meta.client && window.localStorage.getItem(HIST_STORAGE_KEY) === '1'
)
watch(histogramHidden, (v) => {
  if (import.meta.client) {
    window.localStorage.setItem(HIST_STORAGE_KEY, v ? '1' : '0')
  }
})

const subtitle = computed(() => {
  const window = describeWindow(page.range.value)
  return t('logs.subtitle', { count: page.items.value.length, window })
})

function describeWindow(range: TimeWindow): string {
  const f = new Date(range.from)
  const tt = new Date(range.to)
  const fmt = new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' })
  return `${fmt.format(f)} → ${fmt.format(tt)}`
}

const filters: FilterDescriptor[] = [
  // Application stays interactive in live mode: changing it triggers a reload
  // (watcher inside useLogsPage) and the next live tick uses the new filter.
  { kind: 'application', modelValue: page.service, options: page.availableServices },
  { kind: 'time-range', modelValue: page.range, disabled: page.isLive, retentionDays: $logRetentionDays, maxWindowHours: $queryMaxWindowHours },
  { kind: 'severity', modelValue: page.severityFilter },
  { kind: 'attributes', modelValue: page.attributeFilters },
  { kind: 'limit', modelValue: page.limit, disabled: page.isLive }
]

// Export captures whatever is loaded right now — same "export what I'm
// looking at" principle the metrics tree follows. The live tail isn't
// paused; if rows arrive between the click and the file write they're
// included.
const exportDisabled = computed(() => page.items.value.length === 0)
function exportLogs() {
  if (page.items.value.length === 0) return
  const envelope = buildLogsExport(page.items.value)
  downloadOtlpJson(envelope, 'logs')
}

const actions: ActionDescriptor[] = [
  { kind: 'custom', labelKey: 'logs.exportOtlp', icon: 'i-ph-download-simple', onClick: exportLogs, disabled: exportDisabled },
  { kind: 'refresh', loading: page.isLoading, disabled: page.isLive, onClick: page.reload },
  { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive }
]

function clearTraceFilter() {
  page.traceId.value = undefined
  void page.reload()
}

// ISO-ish `yyyy-MM-dd HH:mm:ss.SSS`. Locale-independent on purpose —
// the cell is monospace and the same-shape rendering keeps the column
// scannable when the user filters across multiple days. (A locale
// formatter would shuffle dd/MM/yyyy vs MM/dd/yyyy and lose the
// alignment that makes a long log tail readable.)
function formatTimestamp(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return ''
  const pad2 = (n: number) => n.toString().padStart(2, '0')
  const pad3 = (n: number) => n.toString().padStart(3, '0')
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())} `
    + `${pad2(d.getHours())}:${pad2(d.getMinutes())}:${pad2(d.getSeconds())}`
    + `.${pad3(d.getMilliseconds())}`
}

// Column sizing — paired with AppDataGrid's `autoSizeStrategy: fitGridWidth`.
// The grid scales these `width` values up to fit the viewport on first paint.
// `body` carries the largest target: the actual log message is the column
// that earns visual weight; everything else is metadata.
const columnDefs = computed<ColDef<LogRecordDto>[]>(() => [
  {
    field: 'time',
    headerName: t('logs.col.time'),
    width: 200,
    minWidth: 180,
    sort: 'desc',
    cellClass: 'vellum-cell-mono',
    valueFormatter: p => p.value ? formatTimestamp(p.value as string) : ''
  },
  {
    field: 'serviceName',
    headerName: t('logs.col.service'),
    width: 130,
    minWidth: 100,
    cellClass: 'vellum-cell-mono',
    valueFormatter: p => (p.value as string) ?? '·'
  },
  {
    field: 'severityNumber',
    headerName: t('logs.col.severity'),
    width: 96,
    minWidth: 88,
    cellRenderer: SeverityBadgeCell
  },
  {
    field: 'body',
    headerName: t('logs.col.body'),
    width: 480,
    minWidth: 200,
    tooltipField: 'body',
    cellClass: 'truncate'
  },
  {
    field: 'scopeName',
    headerName: t('logs.col.scope'),
    width: 130,
    minWidth: 100,
    cellClass: 'vellum-cell-muted',
    valueFormatter: p => (p.value as string) ?? '·'
  },
  {
    field: 'traceId',
    headerName: t('logs.col.trace'),
    width: 96,
    minWidth: 80,
    cellRenderer: TraceLinkCell
  }
])

// Stable id: use the longest discriminator we have. The composable dedupes on
// (time, spanId, body[:64]); we mirror that here so AG Grid never collapses
// two distinct logs into one row id.
function rowId(r: LogRecordDto): string {
  return `${r.time}|${r.spanId ?? ''}|${(r.body ?? '').slice(0, 64)}`
}

const selectedId = computed(() => page.selected.value ? rowId(page.selected.value) : null)
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :title="t('logs.title')"
        :subtitle="subtitle"
        :filters="filters"
        :actions="actions"
      >
        <template #filters-extra>
          <AppSearchInput v-model="page.bodyQuery.value" :placeholder="t('filter.searchBody')" />
          <Transition name="scale-fade">
            <button
              v-if="page.traceId.value"
              type="button"
              class="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md border border-primary/40 bg-primary/10 text-primary text-xs hover:bg-primary/20 transition-colors"
              :title="t('logs.removeTraceFilter')"
              @click="clearTraceFilter"
            >
              <UIcon name="i-ph-tree-structure" class="size-3.5" />
              <span class="font-mono">{{ page.traceId.value!.slice(0, 8) }}…</span>
              <UIcon name="i-ph-x" class="size-3.5" />
            </button>
          </Transition>

          <!-- Histogram show/hide. ml-auto pushes the toggle to the
               far right of the filter row, separating it visually from
               the data filters on the left. The icon flips with the
               persisted state so the affordance is always current. -->
          <button
            type="button"
            class="ml-auto inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-muted hover:text-default text-xs transition-colors"
            :title="histogramHidden ? t('logs.histogram.show') : t('logs.histogram.hide')"
            :aria-label="histogramHidden ? t('logs.histogram.show') : t('logs.histogram.hide')"
            @click="histogramHidden = !histogramHidden"
          >
            <UIcon
              :name="histogramHidden ? 'i-ph-chart-bar' : 'i-ph-eye-slash'"
              class="size-3.5"
            />
          </button>
        </template>
      </AppToolbar>
    </template>

    <LogsSeverityHistogram
      v-if="page.items.value.length > 0 && !histogramHidden"
      :data="histogram"
    />

    <AppDataGrid
      :column-defs="columnDefs"
      :row-data="page.items.value"
      :loading="page.isLoading.value"
      :error="page.error.value"
      :get-row-id="rowId"
      :selected-id="selectedId"
      :empty-title="t('logs.emptyTitle')"
      :empty-description="t('logs.emptyDescription')"
      :error-title="t('logs.errorTitle')"
      :live="page.isLive.value"
      :has-more="!page.isLive.value && page.hasMore.value"
      :loading-more="page.isLoading.value && page.items.value.length > 0"
      :row-height="30"
      @row-click="row => page.selected.value = row"
      @retry="page.reload"
      @load-more="page.loadMore"
    />

    <AppDrawer
      name="logs-detail"
      :open="page.selected.value !== null"
      :title="t('logs.detail.title')"
      @update:open="(v: boolean) => { if (!v) page.selected.value = null }"
    >
      <LogDetailContent v-if="page.selected.value" :record="page.selected.value" />
    </AppDrawer>
  </AppPage>
</template>
