<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppResizableSplit from '~/components/overlay/AppResizableSplit.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppApplicationFilter from '~/components/form/AppApplicationFilter.vue'
import InstrumentsTable from './components/InstrumentsTable.vue'
import SeriesPanel from './components/SeriesPanel.vue'
import { useMetricsPage } from './usePage'
import type { ActionDescriptor } from '~/types/toolbar'

const { t } = useI18n()
const { $metricsService } = useNuxtApp()
const page = useMetricsPage($metricsService)

const actions: ActionDescriptor[] = [
  { kind: 'refresh', loading: page.isLoadingList, disabled: page.isLive, onClick: () => page.reloadList() },
  { kind: 'live', isLive: page.isLive, onToggle: page.toggleLive }
]
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar :title="t('metrics.title')" :actions="actions">
        <template #filters-extra>
          <AppApplicationFilter
            :model-value="page.service.value"
            :options="page.availableServices.value"
            :include-all="false"
            :disabled="page.isLive.value"
            :placeholder="t('metrics.selectApplication')"
            @update:model-value="(v) => page.service.value = v"
          />
        </template>
      </AppToolbar>
    </template>

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-lucide-alert-triangle"
      :title="page.error.value"
      class="mb-4"
    />

    <AppEmptyState
      v-if="!page.service.value && page.availableServices.value.length === 0"
      icon="i-lucide-activity"
      :title="t('metrics.noData')"
    />
    <AppEmptyState
      v-else-if="!page.service.value"
      icon="i-lucide-mouse-pointer-click"
      :title="t('metrics.selectApplication')"
    />
    <AppResizableSplit
      v-else
      name="metrics-split"
      :default-ratio="0.45"
    >
      <template #first>
        <InstrumentsTable
          :items="page.instruments.value"
          :loading="page.isLoadingList.value"
          :selected="page.selected.value"
          @select="page.select"
        />
      </template>
      <template #second>
        <SeriesPanel
          :series="page.series.value"
          :loading="page.isLoadingSeries.value"
        />
      </template>
    </AppResizableSplit>
  </AppPage>
</template>
