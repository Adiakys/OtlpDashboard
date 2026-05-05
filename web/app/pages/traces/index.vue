<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import TraceStatusBadgeCell from '~/components/data/cells/TraceStatusBadgeCell.vue'
import DurationBarCell from '~/components/data/cells/DurationBarCell.vue'
import { useTracesPage } from './usePage'
import type {
  ActionDescriptor,
  FilterDescriptor
} from '~/types/toolbar'
import type { TimeWindow, TraceSummaryDto } from '~/services/types'

const { t, locale } = useI18n()
const { $traceService, $traceRetentionDays, $queryMaxWindowHours } = useNuxtApp()
const page = useTracesPage($traceService)

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
  { kind: 'application', modelValue: page.service, options: page.availableServices, includeAll: true },
  { kind: 'time-range', modelValue: page.range, disabled: page.isLive, retentionDays: $traceRetentionDays, maxWindowHours: $queryMaxWindowHours },
  { kind: 'status', modelValue: page.statusFilter },
  { kind: 'duration', modelValue: page.durationFilter },
  { kind: 'attributes', modelValue: page.attributeFilters },
  { kind: 'limit', modelValue: page.limit, disabled: page.isLive }
]

const actions: ActionDescriptor[] = [
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
    width: 130,
    minWidth: 100,
    cellClass: 'vellum-cell-mono',
    valueFormatter: p => (p.value as string) ?? '·'
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
  navigateTo(`/traces/${row.traceId}`)
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
