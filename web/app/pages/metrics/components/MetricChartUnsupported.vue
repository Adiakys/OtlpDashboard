<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import type { MetricPointDto, MetricSeriesDto } from '~/services/types'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const props = defineProps<{
  series: MetricSeriesDto[]
  loading: boolean
}>()

const { t, locale } = useI18n()

function formatValue(value: number): string {
  if (Number.isInteger(value)) return value.toString()
  return value.toFixed(4).replace(/\.?0+$/, '')
}

interface Row extends MetricPointDto {
  instrumentName: string
}

const rows = computed<Row[]>(() => {
  const out: Row[] = []
  for (const s of props.series) {
    for (const p of s.points) {
      out.push({ ...p, instrumentName: s.instrument.name })
    }
  }
  return out
})

const columnDefs = computed<ColDef<Row>[]>(() => [
  {
    field: 'time',
    headerName: t('metrics.col.time'),
    width: 130,
    sort: 'desc',
    cellClass: 'font-mono text-xs items-center flex',
    valueFormatter: p => p.value ? dateTimeFormat(p.value as string, 'time-ms', locale.value) : ''
  },
  {
    field: 'instrumentName',
    headerName: t('metrics.col.instrument'),
    flex: 1,
    minWidth: 200,
    cellClass: 'font-mono text-xs items-center flex'
  },
  {
    field: 'value',
    headerName: t('metrics.col.value'),
    width: 130,
    type: 'rightAligned',
    cellClass: 'font-mono text-xs items-center flex justify-end',
    valueFormatter: p => formatValue(p.value as number)
  },
  {
    field: 'attributes',
    headerName: t('metrics.col.attributes'),
    flex: 1,
    minWidth: 200,
    cellClass: 'font-mono text-xs text-muted items-center flex',
    valueFormatter: p => {
      const a = p.value as Record<string, unknown>
      return Object.keys(a).length === 0 ? '·' : JSON.stringify(a)
    }
  }
])
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col gap-3 p-4">
    <div class="flex items-center gap-3 px-3 py-2 rounded-md border border-warning/40 bg-warning/10 text-warning">
      <UIcon name="i-ph-flask" class="size-4 shrink-0" />
      <p class="text-sm">{{ t('metrics.chart.unsupported') }}</p>
    </div>
    <div class="flex-1 min-h-0 flex flex-col">
      <header class="px-3 py-1.5 text-xs uppercase tracking-wide text-muted">
        {{ t('metrics.chart.rawPoints') }}
      </header>
      <AppDataGrid
        :column-defs="columnDefs"
        :row-data="rows"
        :loading="loading"
        :empty-title="t('metrics.noData')"
      />
    </div>
  </div>
</template>
