<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import { h } from 'vue'
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import AppDrawer from '~/components/overlay/AppDrawer.vue'
import AppBadge from '~/components/ui/AppBadge.vue'
import AppLoadMoreButton from '~/components/ui/AppLoadMoreButton.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import LogDetailContent from './components/LogDetailContent.vue'
import { useLogsPage } from './usePage'
import {
  SEVERITY_BUCKETS,
  severityBucketFromNumber,
  type SeverityBucket
} from '~/types/filters'
import type {
  ActionDescriptor,
  FilterDescriptor
} from '~/types/toolbar'
import type { LogRecordDto, TimeWindow } from '~/services/types'

const { t, locale } = useI18n()
const route = useRoute()
const { $logsService } = useNuxtApp()

function strFromQuery(key: string): string | undefined {
  const v = route.query[key]
  return typeof v === 'string' && v.length > 0 ? v : undefined
}
const initialTraceId = strFromQuery('traceId')
const fromQ = strFromQuery('from')
const toQ = strFromQuery('to')
const initialRange: TimeWindow | undefined = fromQ && toQ ? { from: fromQ, to: toQ } : undefined

const page = useLogsPage($logsService, { initialTraceId, initialRange })

// Frontend-only filters: severity bucket selection (empty = all) and body search.
const severityFilter = ref<SeverityBucket[]>([])
const bodyQuery = ref('')

const filteredItems = computed<LogRecordDto[]>(() => {
  let rows = page.items.value
  if (severityFilter.value.length > 0) {
    const allowed = new Set(severityFilter.value)
    rows = rows.filter(r => allowed.has(severityBucketFromNumber(r.severityNumber)))
  }
  const q = bodyQuery.value.trim().toLowerCase()
  if (q) {
    rows = rows.filter(r => (r.body ?? '').toLowerCase().includes(q))
  }
  return rows
})

const subtitle = computed(() => {
  const window = describeWindow(page.range.value)
  return t('logs.subtitle', { count: filteredItems.value.length, window })
})

function describeWindow(range: TimeWindow): string {
  const f = new Date(range.from)
  const tt = new Date(range.to)
  const fmt = new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' })
  return `${fmt.format(f)} → ${fmt.format(tt)}`
}

const filters: FilterDescriptor[] = [
  { kind: 'application', modelValue: page.service, options: page.availableServices, includeAll: true, disabled: page.isLive },
  { kind: 'time-range', modelValue: page.range, disabled: page.isLive },
  { kind: 'severity', modelValue: severityFilter },
  { kind: 'limit', modelValue: page.limit, disabled: page.isLive }
]

const actions: ActionDescriptor[] = [
  { kind: 'refresh', loading: page.isLoading, disabled: page.isLive, onClick: page.reload },
  { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive }
]

function clearTraceFilter() {
  page.traceId.value = undefined
  void page.reload()
}

const timeFormatter = computed(() => new Intl.DateTimeFormat(locale.value, {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  fractionalSecondDigits: 3
}))

const columnDefs = computed<ColDef<LogRecordDto>[]>(() => [
  {
    field: 'time',
    headerName: t('logs.col.time'),
    width: 130,
    sortable: true,
    sort: 'desc',
    cellClass: 'font-mono text-xs items-center flex',
    valueFormatter: p => p.value ? timeFormatter.value.format(new Date(p.value as string)) : ''
  },
  {
    field: 'serviceName',
    headerName: t('logs.col.service'),
    width: 160,
    cellClass: 'font-mono text-xs items-center flex',
    valueFormatter: p => (p.value as string) ?? '—'
  },
  {
    field: 'severityNumber',
    headerName: t('logs.col.severity'),
    width: 110,
    cellRenderer: (p: { data?: LogRecordDto }) => {
      const row = p.data
      if (!row) return ''
      const bucket = severityBucketFromNumber(row.severityNumber)
      return h(AppBadge, { tone: { kind: 'severity', bucket }, size: 'xs' }, () => row.severityText ?? String(row.severityNumber))
    }
  },
  {
    field: 'body',
    headerName: t('logs.col.body'),
    flex: 1,
    minWidth: 240,
    tooltipField: 'body',
    cellClass: 'truncate items-center flex'
  },
  {
    field: 'scopeName',
    headerName: t('logs.col.scope'),
    width: 160,
    cellClass: 'text-xs text-muted items-center flex',
    valueFormatter: p => (p.value as string) ?? '—'
  },
  {
    field: 'traceId',
    headerName: t('logs.col.trace'),
    width: 80,
    cellRenderer: (p: { data?: LogRecordDto }) => {
      const id = p.data?.traceId
      if (!id) return ''
      return h('a', {
        href: `/traces/${id}`,
        class: 'text-primary inline-flex items-center gap-1 font-mono text-xs hover:underline',
        title: id,
        onClick: (e: MouseEvent) => {
          e.preventDefault()
          e.stopPropagation()
          navigateTo(`/traces/${id}`)
        }
      }, id.slice(0, 8))
    }
  }
])

function rowId(r: LogRecordDto): string {
  return `${r.time}|${r.spanId ?? ''}|${(r.body ?? '').slice(0, 32)}`
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
          <AppSearchInput v-model="bodyQuery" :placeholder="t('filter.searchBody')" />
          <Transition name="scale-fade">
            <button
              v-if="page.traceId.value"
              type="button"
              class="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md border border-primary/40 bg-primary/10 text-primary text-xs hover:bg-primary/20 transition-colors"
              :title="t('logs.removeTraceFilter')"
              @click="clearTraceFilter"
            >
              <UIcon name="i-lucide-waypoints" class="size-3.5" />
              <span class="font-mono">{{ page.traceId.value!.slice(0, 8) }}…</span>
              <UIcon name="i-lucide-x" class="size-3.5" />
            </button>
          </Transition>
        </template>
      </AppToolbar>
    </template>

    <AppDataGrid
      :column-defs="columnDefs"
      :row-data="filteredItems"
      :loading="page.isLoading.value"
      :error="page.error.value"
      :get-row-id="rowId"
      :selected-id="selectedId"
      :empty-title="t('logs.emptyTitle')"
      :empty-description="t('logs.emptyDescription')"
      :error-title="t('logs.errorTitle')"
      @row-click="row => page.selected.value = row"
      @retry="page.reload"
    />

    <AppLoadMoreButton
      v-if="!page.isLive.value"
      class="shrink-0"
      :has-more="page.hasMore.value"
      :loading="page.isLoading.value"
      @load="page.loadMore"
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
