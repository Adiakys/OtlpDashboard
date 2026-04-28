<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import KindBadgeCell from '~/components/data/cells/KindBadgeCell.vue'
import type { InstrumentDto } from '~/services/types'

const props = defineProps<{
  items: InstrumentDto[]
  loading: boolean
  selected: InstrumentDto | null
}>()

const emit = defineEmits<{ select: [instrument: InstrumentDto] }>()

const { t } = useI18n()

function rowId(i: InstrumentDto): string {
  return `${i.resourceHash}|${i.scopeName}|${i.name}|${i.kind}`
}

const selectedId = computed(() => props.selected ? rowId(props.selected) : null)

const columnDefs = computed<ColDef<InstrumentDto>[]>(() => [
  {
    field: 'name',
    headerName: t('metrics.col.instrument'),
    flex: 1,
    minWidth: 180,
    cellClass: 'font-mono text-xs',
    tooltipField: 'name'
  },
  {
    field: 'kind',
    headerName: t('metrics.col.kind'),
    width: 110,
    cellRenderer: KindBadgeCell
  },
  {
    field: 'scopeName',
    headerName: t('metrics.col.scope'),
    flex: 1,
    minWidth: 140,
    cellClass: 'text-xs text-muted',
    valueFormatter: p => (p.value as string) || '—',
    tooltipField: 'scopeName'
  },
  {
    field: 'unit',
    headerName: t('metrics.col.unit'),
    width: 90,
    cellClass: 'font-mono text-xs',
    valueFormatter: p => (p.value as string) || '—'
  },
  {
    field: 'pointCount',
    headerName: t('metrics.col.points'),
    width: 90,
    type: 'rightAligned',
    cellClass: 'font-mono text-xs'
  }
])
</script>

<template>
  <div class="flex flex-col min-h-0">
    <header class="px-3 py-2 border border-default rounded-t-lg border-b-0 text-xs uppercase tracking-wide text-muted bg-elevated/50">
      {{ t('metrics.instrumentsTitle') }}
    </header>
    <AppDataGrid
      :column-defs="columnDefs"
      :row-data="items"
      :loading="loading"
      :get-row-id="rowId"
      :selected-id="selectedId"
      :empty-title="t('metrics.noData')"
      class="!rounded-t-none"
      @row-click="(row: InstrumentDto) => emit('select', row)"
    />
  </div>
</template>
