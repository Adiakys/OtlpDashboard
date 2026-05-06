<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppErrorState from '~/components/ui/AppErrorState.vue'
import ServiceMapGraph from './serviceMap/components/ServiceMapGraph.vue'
import ServiceDetailDrawer from './serviceMap/components/ServiceDetailDrawer.vue'
import { useServiceMapPage } from './serviceMap/composables/useServiceMapPage'
import type { ActionDescriptor, FilterDescriptor } from '~/types/toolbar'
import type { TimeWindow } from '~/services/types'

const { t, locale } = useI18n()
const { $serviceMapService, $traceRetentionDays, $queryMaxWindowHours } = useNuxtApp()

const page = useServiceMapPage($serviceMapService)

const subtitle = computed(() => {
  const win = describeWindow(page.range.value)
  return t('serviceMap.subtitle', {
    nodeCount: page.data.value.nodes.length,
    edgeCount: page.data.value.edges.length,
    window: win
  })
})

function describeWindow(range: TimeWindow): string {
  const f = new Date(range.from)
  const tt = new Date(range.to)
  const fmt = new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' })
  return `${fmt.format(f)} → ${fmt.format(tt)}`
}

// The map is exploratory, not a live monitor — refresh is manual.
// (Live polling would re-tick the simulation every 5s and reshuffle
// node positions just as the user is reading the graph.)
const filters: FilterDescriptor[] = [
  {
    kind: 'time-range',
    modelValue: page.range,
    retentionDays: $traceRetentionDays,
    maxWindowHours: $queryMaxWindowHours
  }
]
const actions: ActionDescriptor[] = [
  { kind: 'refresh', loading: page.isLoading, onClick: () => void page.reload() }
]

const isEmpty = computed(() => !page.isLoading.value && page.data.value.nodes.length === 0)
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :title="t('serviceMap.title')"
        :subtitle="subtitle"
        :filters="filters"
        :actions="actions"
      />
    </template>

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-ph-warning"
      :title="page.error.value"
      class="mb-4"
    />

    <AppEmptyState
      v-if="isEmpty"
      icon="i-ph-graph"
      :title="t('serviceMap.emptyTitle')"
      :description="t('serviceMap.emptyDescription')"
    />

    <ServiceMapGraph
      v-else
      :data="page.data.value"
      :selected="page.selected.value"
      @select="(s) => page.selected.value = s"
    />

    <ServiceDetailDrawer
      :service="page.selected.value"
      :data="page.data.value"
      :range="page.range.value"
      @close="page.selected.value = null"
    />
  </AppPage>
</template>
