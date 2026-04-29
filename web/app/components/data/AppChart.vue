<script setup lang="ts">
import { AgCharts } from 'ag-charts-vue3'
import type { AgChartOptions } from 'ag-charts-community'
import { computed } from 'vue'

/**
 * Theme-aware AG Charts wrapper. The chart options are re-derived only when
 * the input changes (Vue's `computed` already memoizes by referential
 * equality of `props.options`, so widgets that pass a stable computed
 * options object don't pay the re-render cost on unrelated reactivity).
 */
const props = defineProps<{
  options: AgChartOptions
}>()

const colorMode = useColorMode()

const themedOptions = computed<AgChartOptions>(() => ({
  ...props.options,
  theme: colorMode.value === 'dark' ? 'ag-default-dark' : 'ag-default'
}))
</script>

<template>
  <div class="relative w-full h-full min-h-0 min-w-0">
    <AgCharts :options="themedOptions" class="absolute inset-0" />
  </div>
</template>
