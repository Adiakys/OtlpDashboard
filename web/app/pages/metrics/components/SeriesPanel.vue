<script setup lang="ts">
import type { ColDef } from 'ag-grid-community'
import AppDataGrid from '~/components/data/AppDataGrid.vue'
import AppBadge from '~/components/ui/AppBadge.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppSkeleton from '~/components/ui/AppSkeleton.vue'
import type { MetricPointDto, MetricSeriesDto } from '~/services/types'

const props = defineProps<{
  series: MetricSeriesDto | null
  loading: boolean
}>()

const { t, locale } = useI18n()

const formatter = computed(() => new Intl.DateTimeFormat(locale.value, {
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  fractionalSecondDigits: 3
}))

function formatValue(value: number): string {
  if (Number.isInteger(value)) return value.toString()
  return value.toFixed(4).replace(/\.?0+$/, '')
}

const columnDefs = computed<ColDef<MetricPointDto>[]>(() => [
  {
    field: 'time',
    headerName: t('metrics.col.time'),
    width: 130,
    sort: 'desc',
    cellClass: 'font-mono text-xs items-center flex',
    valueFormatter: p => p.value ? formatter.value.format(new Date(p.value as string)) : ''
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
      return Object.keys(a).length === 0 ? '—' : JSON.stringify(a)
    }
  }
])
</script>

<template>
  <div class="flex flex-col min-h-0 ml-1">
    <header class="px-3 py-2 border border-default rounded-t-lg border-b-0 text-xs uppercase tracking-wide text-muted bg-elevated/50">
      {{ t('metrics.seriesTitle') }}
    </header>

    <div
      v-if="!series && !loading"
      class="flex-1 min-h-0 border border-default rounded-b-lg border-t-0 bg-default flex items-center justify-center"
    >
      <AppEmptyState
        icon="i-lucide-mouse-pointer-click"
        :title="t('metrics.noSelection')"
      />
    </div>
    <div
      v-else-if="loading && !series"
      class="flex-1 min-h-0 border border-default rounded-b-lg border-t-0 bg-default p-4"
    >
      <AppSkeleton :rows="6" row-class="h-6" />
    </div>
    <template v-else-if="series">
      <div class="border-x border-default bg-elevated/40 px-4 py-3">
        <div class="flex items-baseline justify-between gap-3">
          <h2 class="font-mono text-sm font-semibold truncate" :title="series.instrument.name">
            {{ series.instrument.name }}
          </h2>
          <AppBadge size="xs">{{ series.instrument.kind }}</AppBadge>
        </div>
        <p v-if="series.instrument.description" class="text-xs text-muted mt-1">
          {{ series.instrument.description }}
        </p>
        <dl class="grid grid-cols-2 gap-x-4 gap-y-0.5 mt-2 text-xs">
          <dt class="text-muted">{{ t('metrics.col.unit') }}</dt>
          <dd class="font-mono">{{ series.instrument.unit || '—' }}</dd>
          <dt class="text-muted">Temporality</dt>
          <dd>{{ series.instrument.temporality }}</dd>
          <dt class="text-muted">Monotonic</dt>
          <dd>{{ series.instrument.isMonotonic ? t('common.yes') : t('common.no') }}</dd>
          <dt class="text-muted">{{ t('metrics.col.scope') }}</dt>
          <dd class="font-mono truncate" :title="series.instrument.scopeName">
            {{ series.instrument.scopeName || '—' }}
          </dd>
        </dl>
      </div>
      <AppDataGrid
        :column-defs="columnDefs"
        :row-data="series.points"
        :loading="loading"
        :empty-title="t('metrics.noData')"
        class="!rounded-t-none"
      />
    </template>
  </div>
</template>
