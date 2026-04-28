<script setup lang="ts">
import type { AgChartOptions } from 'ag-charts-community'
import AppChart from '~/components/data/AppChart.vue'

defineProps<{
  options: AgChartOptions
  loading: boolean
}>()
</script>

<template>
  <div class="flex-1 min-h-0 relative bg-default">
    <AppChart :options="options" />

    <Transition
      enter-active-class="transition-opacity duration-150"
      leave-active-class="transition-opacity duration-150"
      enter-from-class="opacity-0"
      leave-to-class="opacity-0"
    >
      <div
        v-if="loading"
        class="absolute top-2 right-2 inline-flex items-center gap-1.5 px-2 py-1 rounded bg-elevated/80 text-xs text-muted"
      >
        <UIcon name="i-lucide-loader-2" class="size-3.5 animate-spin" />
        <span>{{ $t('common.loading') }}</span>
      </div>
    </Transition>
  </div>
</template>
