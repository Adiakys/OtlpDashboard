<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppResizableSplit from '~/components/overlay/AppResizableSplit.vue'
import AppSearchInput from '~/components/form/AppSearchInput.vue'
import MetricsTree from './components/MetricsTree.vue'
import MetricChartPanel from './components/MetricChartPanel.vue'
import { useMetricsPage } from './usePage'
import type { ActionDescriptor, FilterDescriptor } from '~/types/toolbar'
import type { TimeWindow, MetricSeriesDto } from '~/services/types'
import { dateTimeFormat } from '~/lib/dateTimeFormat'

const { t, locale } = useI18n()
const { $metricsService, $metricRetentionDays, $queryMaxWindowHours } = useNuxtApp()
const page = useMetricsPage($metricsService)

const filters: FilterDescriptor[] = [
  // Time range stays disabled in live mode (the live tick refreshes the
  // ringbuffer with whatever the server has).
  { kind: 'time-range', modelValue: page.range, disabled: page.isLive, retentionDays: $metricRetentionDays, maxWindowHours: $queryMaxWindowHours }
]

const actions: ActionDescriptor[] = [
  { kind: 'refresh', loading: page.isLoadingList, disabled: page.isLive, onClick: () => page.reloadList() },
  { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive }
]

// Tick `now` while in live mode so the subtitle reflects the sliding window
// the composable is actually querying. Outside live mode the timer is idle
// and the subtitle shows the static range the user picked.
const now = ref(new Date())
let nowTimer: ReturnType<typeof setInterval> | null = null
function startNowTicker() {
  if (nowTimer) return
  nowTimer = setInterval(() => { now.value = new Date() }, 1000)
}
function stopNowTicker() {
  if (nowTimer) { clearInterval(nowTimer); nowTimer = null }
}
watch(() => page.isLive.value, live => {
  if (live) startNowTicker()
  else stopNowTicker()
}, { immediate: true })
onBeforeUnmount(stopNowTicker)

const displayedRange = computed<TimeWindow>(() => {
  if (!page.isLive.value) return page.range.value
  const fromMs = new Date(page.range.value.from).getTime()
  const toMs = new Date(page.range.value.to).getTime()
  const duration = Number.isFinite(fromMs) && Number.isFinite(toMs) && toMs > fromMs
    ? toMs - fromMs
    : 60 * 60 * 1000
  const nowMs = now.value.getTime()
  return {
    from: new Date(nowMs - duration).toISOString(),
    to: now.value.toISOString()
  }
})

const subtitle = computed(() => t('metrics.subtitle', {
  count: page.selectedKeys.value.size,
  window: describeWindow(displayedRange.value)
}))

function describeWindow(range: TimeWindow): string {
  // `datetime-seconds` keeps seconds in the label, which matters in live
  // mode where the window slides every second.
  return `${dateTimeFormat(range.from, 'datetime-seconds', locale.value)} → ${dateTimeFormat(range.to, 'datetime-seconds', locale.value)}`
}

const orderedSeries = computed<MetricSeriesDto[]>(() => {
  const out: MetricSeriesDto[] = []
  for (const key of page.selectedKeys.value) {
    const s = page.series.value.get(key)
    if (s) out.push(s)
  }
  return out
})
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar :title="t('metrics.title')" :subtitle="subtitle" :filters="filters" :actions="actions">
        <template #filters-extra>
          <AppSearchInput
            :model-value="page.searchQuery.value"
            :placeholder="t('metrics.tree.search')"
            @update:model-value="(v) => page.searchQuery.value = v"
          />
        </template>
      </AppToolbar>
    </template>

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-ph-warning"
      :title="page.error.value"
      class="mb-4"
    />

    <AppResizableSplit
      name="metrics-split"
      :default-ratio="0.32"
      :min-ratio="0.2"
      :max-ratio="0.6"
    >
      <template #first>
        <div class="flex flex-col h-full min-h-0">
          <MetricsTree
            :tree="page.tree.value"
            :loading="page.isLoadingList.value"
            :is-selected="page.isSelected"
            :is-compatible="page.isCompatible"
            @toggle-leaf="page.toggleSelection"
          />
        </div>
      </template>
      <template #second>
        <div class="flex flex-col h-full min-h-0">
          <MetricChartPanel
            :selected="page.selectedInstruments.value"
            :loaded-series="orderedSeries"
            :unit="page.selectedUnit.value"
            :units="page.selectedUnits.value"
            :chart-type="page.chartType.value"
            :chart-options="page.chartOptions.value"
            :split-by="page.splitBy.value"
            :available-attributes="page.availableAttributes.value"
            :loading="page.isLoadingSeries.value"
            @update:split-by="(v) => page.splitBy.value = v"
            @remove="page.removeSelection"
            @clear-all="page.clearSelection"
          />
        </div>
      </template>
    </AppResizableSplit>
  </AppPage>
</template>
