<script setup lang="ts">
import { AgCharts } from 'ag-charts-vue3'
import type { AgChartOptions } from 'ag-charts-community'

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
