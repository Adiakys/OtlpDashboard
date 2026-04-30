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
import type { DurationRange, TraceStatusFilter } from '~/types/filters'
import type { TimeWindow, TraceSummaryDto } from '~/services/types'

const { t, locale } = useI18n()
const { $traceService } = useNuxtApp()
const page = useTracesPage($traceService)

const statusFilter = ref<TraceStatusFilter>('any')
const durationFilter = ref<DurationRange>({ minMs: null, maxMs: null })
const searchQuery = ref('')

const filteredItems = computed<TraceSummaryDto[]>(() => {
  let rows = page.items.value
  if (statusFilter.value === 'ok') rows = rows.filter(r => r.rootStatusCode === 'Ok')
  else if (statusFilter.value === 'error') rows = rows.filter(r => r.rootStatusCode === 'Error')
  const { minMs, maxMs } = durationFilter.value
  if (minMs != null) rows = rows.filter(r => r.durationMs >= minMs)
  if (maxMs != null) rows = rows.filter(r => r.durationMs <= maxMs)
  const q = searchQuery.value.trim().toLowerCase()
  if (q) rows = rows.filter(r => r.rootSpanName.toLowerCase().includes(q))
  return rows
})

const maxDuration = computed(() => filteredItems.value.reduce((m, r) => Math.max(m, r.durationMs), 1))

const subtitle = computed(() => t('traces.subtitle', {
  count: filteredItems.value.length,
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
  { kind: 'time-range', modelValue: page.range, disabled: page.isLive },
  { kind: 'status', modelValue: statusFilter },
  { kind: 'duration', modelValue: durationFilter },
  { kind: 'limit', modelValue: page.limit, disabled: page.isLive }
]

const actions: ActionDescriptor[] = [
  { kind: 'refresh', loading: page.isLoading, disabled: page.isLive, onClick: page.reload },
  { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive }
]

const timeFormatter = computed(() => new Intl.DateTimeFormat(locale.value, {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit'
}))

const columnDefs = computed<ColDef<TraceSummaryDto>[]>(() => [
  {
    field: 'start',
    headerName: t('traces.col.start'),
    width: 110,
    sort: 'desc',
    cellClass: 'font-mono text-xs',
    valueFormatter: p => p.value ? timeFormatter.value.format(new Date(p.value as string)) : ''
  },
  {
    field: 'serviceName',
    headerName: t('traces.col.service'),
    width: 160,
    cellClass: 'font-mono text-xs',
    valueFormatter: p => (p.value as string) ?? '·'
  },
  {
    field: 'rootSpanName',
    headerName: t('traces.col.rootSpan'),
    flex: 1,
    minWidth: 200
  },
  {
    field: 'durationMs',
    headerName: t('traces.col.duration'),
    width: 200,
    cellRenderer: DurationBarCell,
    cellRendererParams: { max: () => maxDuration.value }
  },
  {
    field: 'spanCount',
    headerName: t('traces.col.spans'),
    width: 80,
    cellClass: 'font-mono text-xs',
    type: 'rightAligned'
  },
  {
    field: 'rootStatusCode',
    headerName: t('traces.col.status'),
    width: 110,
    cellRenderer: TraceStatusBadgeCell
  },
  {
    field: 'traceId',
    headerName: t('traces.col.traceId'),
    width: 140,
    cellClass: 'font-mono text-xs text-muted',
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
          <AppSearchInput v-model="searchQuery" :placeholder="t('filter.searchTrace')" />
        </template>
      </AppToolbar>
    </template>

    <AppDataGrid
      :column-defs="columnDefs"
      :row-data="filteredItems"
      :loading="page.isLoading.value"
      :error="page.error.value"
      :get-row-id="(r: TraceSummaryDto) => r.traceId"
      :empty-title="t('traces.emptyTitle')"
      :empty-description="t('traces.emptyDescription')"
      :error-title="t('traces.errorTitle')"
      :row-height="40"
      :has-more="!page.isLive.value && page.hasMore.value"
      :loading-more="page.isLoading.value && page.items.value.length > 0"
      @row-click="onRowClick"
      @retry="page.reload"
      @load-more="page.loadMore"
    />
  </AppPage>
</template>
