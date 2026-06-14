<script setup lang="ts">
import { AgCharts } from 'ag-charts-vue3'
import type { AgChartOptions } from 'ag-charts-community'
import { computed } from 'vue'
import { vellumTheme } from '~/lib/agcharts/theme'
import { applyDateTimeDefaults } from '~/lib/agcharts/dateTimeDefaults'

/**
 * Theme-aware AG Charts wrapper. Always applies the Vellum custom theme,
 * regardless of what `theme` was set upstream — chartStrategy.ts sets a
 * placeholder string, this component overrides with the actual theme object
 * built from current dark/light state. Also normalizes time formatting so
 * every chart honors the OS 12h/24h preference (see applyDateTimeDefaults).
 */
const props = defineProps<{
  options: AgChartOptions
}>()

const colorMode = useColorMode()
const { locale } = useI18n()

const themedOptions = computed<AgChartOptions>(() => ({
  ...applyDateTimeDefaults(props.options, locale.value),
  theme: vellumTheme(colorMode.value === 'dark')
}))
</script>

<template>
  <div class="relative w-full h-full min-h-0 min-w-0">
    <AgCharts :options="themedOptions" class="absolute inset-0" />
  </div>
</template>
