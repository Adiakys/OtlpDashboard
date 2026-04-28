<script setup lang="ts">
import { useTracesPage } from './usePage'
import TracesToolbar from './components/TracesToolbar.vue'
import TracesTable from './components/TracesTable.vue'

const { $traceService } = useNuxtApp()
const page = useTracesPage($traceService)
</script>

<template>
  <div class="h-full flex flex-col gap-4">
    <h1 class="text-xl font-semibold">
      Traces
    </h1>

    <TracesToolbar
      v-model:range="page.range.value"
      v-model:limit="page.limit.value"
      v-model:service="page.service.value"
      :live="page.isLive.value"
      :services="page.availableServices.value"
      @reload="page.reload"
      @toggle-live="page.toggleLive"
    />

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-lucide-alert-triangle"
      :title="page.error.value"
    />

    <TracesTable
      class="flex-1 min-h-0"
      :items="page.items.value"
      :loading="page.isLoading.value"
      :has-more="!page.isLive.value && page.hasMore.value"
      @load-more="page.loadMore"
    />
  </div>
</template>
