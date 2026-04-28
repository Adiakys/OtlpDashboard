<script setup lang="ts" generic="TRow extends Record<string, unknown> | object">
import { AgGridVue } from 'ag-grid-vue3'
import type {
  BodyScrollEvent,
  ColDef,
  GridApi,
  GridReadyEvent,
  RowClickedEvent,
  RowDataUpdatedEvent,
  SelectionChangedEvent
} from 'ag-grid-community'
import AppErrorState from '~/components/ui/AppErrorState.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'

const props = withDefaults(defineProps<{
  columnDefs: ColDef<TRow>[]
  rowData: TRow[]
  loading?: boolean
  error?: string | null
  /** Stable id per row, required by AG Grid to diff rowData updates without
   *  recreating DOM nodes (which would reset the scroll position). */
  getRowId?: (row: TRow) => string
  emptyTitle?: string
  emptyDescription?: string
  errorTitle?: string
  /** Selection: single row by id (kept here so the parent can drive a drawer). */
  selectedId?: string | null
  /** Row height override. */
  rowHeight?: number
  /** When true, the grid emits 'loadMore' as the user scrolls toward the bottom. */
  hasMore?: boolean
  /** When true the grid is currently fetching the next page (shows footer hint). */
  loadingMore?: boolean
  /** How many rows from the end trigger the next fetch. */
  loadMoreThreshold?: number
}>(), {
  loading: false,
  error: null,
  selectedId: null,
  hasMore: false,
  loadingMore: false,
  loadMoreThreshold: 8
})

const emit = defineEmits<{
  'rowClick': [row: TRow]
  'selectionChange': [row: TRow | null]
  'retry': []
  'loadMore': []
}>()

const { t } = useI18n()
const colorMode = useColorMode()
const themeClass = computed(() => colorMode.value === 'dark' ? 'ag-theme-quartz-dark' : 'ag-theme-quartz')

const defaultColDef = computed<ColDef>(() => ({
  resizable: true,
  sortable: true,
  filter: false,
  minWidth: 80
}))

const gridApi = shallowRef<GridApi<TRow> | null>(null)

function onGridReady(ev: GridReadyEvent<TRow>) {
  gridApi.value = ev.api
  applySelection()
}

function onRowClicked(ev: RowClickedEvent<TRow>) {
  if (ev.data) emit('rowClick', ev.data)
}

function onSelectionChanged(_ev: SelectionChangedEvent<TRow>) {
  const rows = gridApi.value?.getSelectedRows() ?? []
  emit('selectionChange', rows[0] ?? null)
}

function applySelection() {
  if (!gridApi.value || !props.getRowId) return
  const targetId = props.selectedId
  gridApi.value.forEachNode((node) => {
    const id = node.id ?? null
    const shouldSelect = id != null && id === targetId
    if (node.isSelected() !== shouldSelect) node.setSelected(shouldSelect, false)
  })
}

// Throttle the loadMore signal so the grid doesn't emit dozens of times during
// a single fast scroll. The page handler is also expected to ignore subsequent
// emissions while a fetch is in-flight — defence in depth.
let lastLoadMoreAt = 0
function maybeRequestMore() {
  if (!props.hasMore || props.loadingMore || !gridApi.value) return
  const now = Date.now()
  if (now - lastLoadMoreAt < 250) return

  const total = gridApi.value.getDisplayedRowCount()
  if (total === 0) return
  const last = gridApi.value.getLastDisplayedRowIndex()
  const remaining = total - 1 - last
  if (remaining <= props.loadMoreThreshold) {
    lastLoadMoreAt = now
    emit('loadMore')
  }
}

function onBodyScroll(_ev: BodyScrollEvent<TRow>) {
  maybeRequestMore()
}

function onViewportChanged() {
  maybeRequestMore()
}

function onRowDataUpdated(_ev: RowDataUpdatedEvent<TRow>) {
  // After AG Grid finishes diffing rowData (added/removed/updated rows), make
  // sure the selection still matches and check whether we're already at the
  // bottom (e.g. first page didn't fill the viewport).
  applySelection()
  maybeRequestMore()
}

watch(() => props.selectedId, applySelection)

defineExpose({
  gridApi,
  /** Imperative transactional update, useful for high-frequency live ticks
   *  where rebuilding the rowData array would be wasteful. */
  applyTransaction: (tx: { add?: TRow[]; update?: TRow[]; remove?: TRow[] }) => {
    gridApi.value?.applyTransaction(tx)
  }
})

// Three failure modes:
//   - fatal: error AND no data to fall back to → full-screen AppErrorState.
//   - transient: error but we still have rows (e.g. live polling failed once)
//     → small banner above the grid, grid stays interactive.
//   - empty: no error, no rows, not loading → AG Grid's no-rows overlay drives
//     a localized AppEmptyState rendered as overlay (grid stays mounted).
const showFatalError = computed(() => !!props.error && props.rowData.length === 0 && !props.loading)
const showTransientError = computed(() => !!props.error && props.rowData.length > 0)
const showEmptyOverlay = computed(() => !props.loading && !props.error && props.rowData.length === 0)
</script>

<template>
  <div class="relative flex-1 min-h-0 flex flex-col border border-default rounded-lg overflow-hidden bg-default">
    <AppErrorState
      v-if="showFatalError"
      :title="errorTitle"
      :description="error ?? undefined"
      @retry="emit('retry')"
    />

    <template v-else>
      <Transition name="fade">
        <div
          v-if="showTransientError"
          class="shrink-0 border-b border-default bg-error/10 text-error text-xs px-3 py-1.5 flex items-center gap-2"
        >
          <UIcon name="i-lucide-alert-triangle" class="size-3.5" />
          <span class="truncate">{{ error }}</span>
        </div>
      </Transition>

      <div class="flex-1 min-h-0 flex flex-col">
        <div class="flex-1 min-h-0 relative">
          <AgGridVue
            :class="themeClass"
            style="position: absolute; inset: 0; height: 100%; width: 100%;"
            :column-defs="columnDefs"
            :row-data="rowData"
            :loading="loading && rowData.length === 0"
            :default-col-def="defaultColDef"
            :get-row-id="getRowId ? (params: any) => getRowId!(params.data) : undefined"
            :animate-rows="true"
            :suppress-cell-focus="true"
            :row-selection="'single'"
            :row-height="rowHeight"
            :tooltip-show-delay="300"
            @grid-ready="onGridReady"
            @row-clicked="onRowClicked"
            @selection-changed="onSelectionChanged"
            @body-scroll="onBodyScroll"
            @viewport-changed="onViewportChanged"
            @row-data-updated="onRowDataUpdated"
          />

          <!-- Empty overlay rendered above the grid surface; the grid itself
               stays mounted so the scroll position survives transitions. -->
          <Transition name="fade">
            <div
              v-if="showEmptyOverlay"
              class="absolute inset-0 flex items-center justify-center bg-default/95 pointer-events-auto"
            >
              <AppEmptyState
                :title="emptyTitle ?? ''"
                :description="emptyDescription"
                icon="i-lucide-inbox"
              />
            </div>
          </Transition>
        </div>

        <Transition name="fade">
          <footer
            v-if="loadingMore || (!hasMore && rowData.length > 0)"
            class="shrink-0 px-3 py-1.5 text-xs text-muted text-center border-t border-default bg-elevated/30"
          >
            <span v-if="loadingMore" class="inline-flex items-center gap-2">
              <UIcon name="i-lucide-loader-2" class="size-3.5 animate-spin" />
              {{ t('common.loading') }}
            </span>
            <span v-else>{{ t('common.endOfResults') }}</span>
          </footer>
        </Transition>
      </div>
    </template>
  </div>
</template>
