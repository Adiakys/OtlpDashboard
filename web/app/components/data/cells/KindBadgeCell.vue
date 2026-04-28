<script setup lang="ts">
import type { ICellRendererParams } from 'ag-grid-community'
import AppBadge from '~/components/ui/AppBadge.vue'

const props = defineProps<{ params: ICellRendererParams<unknown, string> }>()

const value = computed(() => (props.params.value as string) ?? '')

const tone = computed<'primary' | 'success' | 'neutral'>(() => {
  if (value.value === 'Gauge') return 'primary'
  if (value.value === 'Sum') return 'success'
  return 'neutral'
})
</script>

<template>
  <AppBadge :tone="tone" size="xs">{{ value }}</AppBadge>
</template>
