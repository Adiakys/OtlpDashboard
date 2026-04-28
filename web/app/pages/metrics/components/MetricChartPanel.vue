<script setup lang="ts">
import type { AgChartOptions } from 'ag-charts-community'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import MetricChartHeader from './MetricChartHeader.vue'
import MetricChartCanvas from './MetricChartCanvas.vue'
import MetricChartUnsupported from './MetricChartUnsupported.vue'
import type { ChartType } from '~/lib/agcharts/chartStrategy'
import type { SplitBy } from '~/lib/agcharts/seriesGrouping'
import type { InstrumentDto, MetricSeriesDto } from '~/services/types'

const props = defineProps<{
  selected: InstrumentDto[]
  loadedSeries: MetricSeriesDto[]
  unit: string | null
  units: (string | null)[]
  chartType: ChartType
  chartOptions: AgChartOptions
  splitBy: SplitBy
  availableAttributes: string[]
  loading: boolean
}>()

const emit = defineEmits<{
  'update:splitBy': [value: SplitBy]
  remove: [key: string]
  'clear-all': []
}>()

const { t } = useI18n()
</script>

<template>
  <div class="flex flex-col h-full min-h-0 ml-1 border border-default rounded-lg bg-default overflow-hidden">
    <template v-if="selected.length === 0">
      <header class="px-3 py-2 border-b border-default bg-elevated/40 text-xs uppercase tracking-wide text-muted">
        {{ t('metrics.chart.title') }}
      </header>
      <AppEmptyState
        icon="i-lucide-line-chart"
        :title="t('metrics.chart.empty')"
      />
    </template>

    <template v-else>
      <MetricChartHeader
        :selected="selected"
        :unit="unit"
        :units="units"
        :split-by="splitBy"
        :available-attributes="availableAttributes"
        @update:split-by="(v) => emit('update:splitBy', v)"
        @remove="(k) => emit('remove', k)"
        @clear-all="emit('clear-all')"
      />

      <Transition
        mode="out-in"
        enter-active-class="transition-opacity duration-200"
        leave-active-class="transition-opacity duration-200"
        enter-from-class="opacity-0"
        leave-to-class="opacity-0"
      >
        <MetricChartUnsupported
          v-if="chartType === 'unsupported'"
          :series="loadedSeries"
          :loading="loading"
        />
        <MetricChartCanvas
          v-else
          :options="chartOptions"
          :loading="loading"
        />
      </Transition>
    </template>
  </div>
</template>
