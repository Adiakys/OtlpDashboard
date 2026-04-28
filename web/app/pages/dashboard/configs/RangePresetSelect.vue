<script setup lang="ts">
import { RANGE_PRESETS, type RangePreset } from '../types'

defineProps<{
  modelValue: RangePreset
}>()

defineEmits<{
  'update:modelValue': [value: RangePreset]
}>()

const { t } = useI18n()

const items = computed(() =>
  RANGE_PRESETS.map(p => ({
    label: t(`dashboard.range.${rangeKey(p)}`),
    value: p
  }))
)

function rangeKey(p: RangePreset): string {
  switch (p) {
    case 'last-5m': return 'last5m'
    case 'last-15m': return 'last15m'
    case 'last-1h': return 'last1h'
    case 'last-6h': return 'last6h'
    case 'last-24h': return 'last24h'
  }
}
</script>

<template>
  <USelectMenu
    :model-value="modelValue"
    :items="items"
    value-key="value"
    @update:model-value="(v) => $emit('update:modelValue', v as RangePreset)"
  />
</template>
