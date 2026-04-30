<script setup lang="ts">
import { AgCharts } from 'ag-charts-vue3'
import type { AgChartOptions } from 'ag-charts-community'
import { computed } from 'vue'
import { vellumTheme } from '~/lib/agcharts/theme'

/**
 * Theme-aware AG Charts wrapper. Always applies the Vellum custom theme,
 * regardless of what `theme` was set upstream — chartStrategy.ts sets a
 * placeholder string, this component overrides with the actual theme object
 * built from current dark/light state.
 */
const props = defineProps<{
  options: AgChartOptions
}>()

const colorMode = useColorMode()

const themedOptions = computed<AgChartOptions>(() => ({
  ...props.options,
  theme: vellumTheme(colorMode.value === 'dark')
}))
</script>

<template>
  <div class="relative w-full h-full min-h-0 min-w-0">
    <AgCharts :options="themedOptions" class="absolute inset-0" />
  </div>
</template>
