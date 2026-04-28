<script setup lang="ts" generic="TRow extends Record<string, unknown> | object">
import { AgGridVue } from 'ag-grid-vue3'
import type {
  ColDef,
  GridApi,
  GridReadyEvent,
  RowClickedEvent,
  SelectionChangedEvent
} from 'ag-grid-community'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppErrorState from '~/components/ui/AppErrorState.vue'
import AppSkeleton from '~/components/ui/AppSkeleton.vue'

const props = withDefaults(defineProps<{
  columnDefs: ColDef<TRow>[]
  rowData: TRow[]
  loading?: boolean
  error?: string | null
  /** Stable id per row, required for animation + selection. */
  getRowId?: (row: TRow) => string
  /** Empty/error/loading copy. */
  emptyTitle?: string
  emptyDescription?: string
  errorTitle?: string
  /** Selection: single row by id (kept here so the parent can drive a drawer). */
  selectedId?: string | null
  /** Row height override. */
  rowHeight?: number
}>(), {
  loading: false,
  error: null,
  selectedId: null
})

const emit = defineEmits<{
  'rowClick': [row: TRow]
  'selectionChange': [row: TRow | null]
  'retry': []
}>()

const colorMode = useColorMode()
const themeClass = computed(() => colorMode.value === 'dark' ? 'ag-theme-quartz-dark' : 'ag-theme-quartz')

const defaultColDef = computed<ColDef>(() => ({
  resizable: true,
  sortable: true,
  filter: false,
  minWidth: 80,
  cellClass: 'flex items-center'
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

watch(() => props.selectedId, applySelection)
watch(() => props.rowData, () => nextTick(applySelection))

defineExpose({ gridApi })

const showEmpty = computed(() => !props.loading && !props.error && props.rowData.length === 0)
const showError = computed(() => !props.loading && !!props.error)
</script>

<template>
  <div class="relative flex-1 min-h-0 flex flex-col border border-default rounded-lg overflow-hidden bg-default">
    <Transition name="fade" mode="out-in">
      <div v-if="loading && rowData.length === 0" key="loading" class="flex-1 min-h-0 p-4">
        <AppSkeleton :rows="8" row-class="h-8" />
      </div>
      <AppErrorState
        v-else-if="showError"
        key="error"
        :title="errorTitle"
        :description="error ?? undefined"
        @retry="emit('retry')"
      />
      <AppEmptyState
        v-else-if="showEmpty"
        key="empty"
        :title="emptyTitle ?? ''"
        :description="emptyDescription"
        icon="i-lucide-inbox"
      />
      <div v-else key="grid" class="flex-1 min-h-0 flex">
        <AgGridVue
          class="flex-1 min-h-0"
          :class="themeClass"
          :column-defs="columnDefs"
          :row-data="rowData"
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
        />
      </div>
    </Transition>
  </div>
</template>
