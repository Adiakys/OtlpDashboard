<script setup lang="ts">
import { useMetricsPage } from './usePage'
import InstrumentsTable from './components/InstrumentsTable.vue'
import SeriesPanel from './components/SeriesPanel.vue'

const { $metricsService } = useNuxtApp()
const page = useMetricsPage($metricsService)
</script>

<template>
  <div class="h-full flex flex-col gap-4">
    <div class="flex items-end justify-between gap-4 flex-wrap">
      <div class="flex items-end gap-4">
        <h1 class="text-xl font-semibold pb-1">
          Metrics
        </h1>
        <ApplicationFilter
          v-model="page.service.value"
          :options="page.availableServices.value"
          :include-all="false"
          :disabled="page.isLive.value"
          placeholder="Select an application"
        />
      </div>
      <div class="flex items-center gap-2">
        <UButton
          size="sm"
          color="neutral"
          variant="subtle"
          icon="i-lucide-refresh-cw"
          :loading="page.isLoadingList.value"
          :disabled="page.isLive.value"
          @click="() => page.reloadList()"
        >
          Refresh
        </UButton>
        <LiveToggle :is-live="page.isLive.value" @toggle="page.toggleLive" />
      </div>
    </div>

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-lucide-alert-triangle"
      :title="page.error.value"
    />

    <div
      v-if="!page.service.value && page.availableServices.value.length === 0"
      class="flex-1 min-h-0 border border-default rounded p-6 text-sm text-muted text-center"
    >
      No metrics have been received yet. Start your OTLP exporter and the
      application filter will populate automatically.
    </div>

    <div
      v-else-if="!page.service.value"
      class="flex-1 min-h-0 border border-default rounded p-6 text-sm text-muted text-center"
    >
      Select an application above to view its instruments.
    </div>

    <div v-else class="flex-1 min-h-0 grid grid-cols-1 lg:grid-cols-2 gap-4">
      <InstrumentsTable
        class="min-h-0"
        :items="page.instruments.value"
        :loading="page.isLoadingList.value"
        :selected="page.selected.value"
        @select="page.select"
      />
      <SeriesPanel
        class="min-h-0"
        :series="page.series.value"
        :loading="page.isLoadingSeries.value"
      />
    </div>
  </div>
</template>
