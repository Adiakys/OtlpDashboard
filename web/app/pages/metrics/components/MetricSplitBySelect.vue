<script setup lang="ts">
import type { SplitBy } from '~/lib/agcharts/seriesGrouping'

const ALL = '__all__'
const AGG = '__agg__'

const props = defineProps<{
  modelValue: SplitBy
  available: string[]
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: SplitBy] }>()

const { t } = useI18n()

const selectedString = computed<string>({
  get: () => {
    if (props.modelValue === 'all') return ALL
    if (props.modelValue.length === 0) return AGG
    return props.modelValue.join(',')
  },
  set: (v) => {
    if (v === ALL) emit('update:modelValue', 'all')
    else if (v === AGG) emit('update:modelValue', [])
    else emit('update:modelValue', v.split(',').filter(s => s.length > 0))
  }
})

const items = computed(() => {
  const out: Array<{ label: string; value: string }> = [
    { label: t('metrics.splitBy.all'), value: ALL },
    { label: t('metrics.splitBy.aggregated'), value: AGG }
  ]
  for (const k of props.available) out.push({ label: k, value: k })
  return out
})
</script>

<template>
  <div class="flex items-center gap-2">
    <span class="text-xs text-muted shrink-0">{{ t('metrics.splitBy.label') }}</span>
    <USelect
      v-model="selectedString"
      :items="items"
      :disabled="disabled || available.length === 0"
      icon="i-ph-columns"
      size="sm"
      class="min-w-44"
    />
  </div>
</template>
