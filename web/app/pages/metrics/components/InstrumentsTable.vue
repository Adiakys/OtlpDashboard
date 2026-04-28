<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import { h } from 'vue'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import AppBadge from '~/components/ui/AppBadge.vue'
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

function kindTone(kind: string): 'primary' | 'success' | 'neutral' {
  if (kind === 'Gauge') return 'primary'
  if (kind === 'Sum') return 'success'
  return 'neutral'
}

const columnDefs = computed<ColDef<InstrumentDto>[]>(() => [
  {
    field: 'name',
    headerName: t('metrics.col.instrument'),
    flex: 1,
    minWidth: 180,
    cellClass: 'font-mono text-xs items-center flex',
    tooltipField: 'name'
  },
  {
    field: 'kind',
    headerName: t('metrics.col.kind'),
    width: 110,
    cellRenderer: (p: { value: string }) => h(AppBadge, { tone: kindTone(p.value), size: 'xs' }, () => p.value)
  },
  {
    field: 'scopeName',
    headerName: t('metrics.col.scope'),
    flex: 1,
    minWidth: 140,
    cellClass: 'text-xs text-muted items-center flex',
    valueFormatter: p => (p.value as string) || '—',
    tooltipField: 'scopeName'
  },
  {
    field: 'unit',
    headerName: t('metrics.col.unit'),
    width: 90,
    cellClass: 'font-mono text-xs items-center flex',
    valueFormatter: p => (p.value as string) || '—'
  },
  {
    field: 'pointCount',
    headerName: t('metrics.col.points'),
    width: 90,
    type: 'rightAligned',
    cellClass: 'font-mono text-xs items-center flex justify-end'
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
