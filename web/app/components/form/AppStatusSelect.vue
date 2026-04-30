<script setup lang="ts">
import type { TraceStatusFilter } from '~/types/filters'

defineProps<{
  modelValue: TraceStatusFilter
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: TraceStatusFilter] }>()

const { t } = useI18n()

const items = computed(() => [
  { label: t('filter.statusAny'), value: 'any' as const },
  { label: t('filter.statusOk'), value: 'ok' as const },
  { label: t('filter.statusError'), value: 'error' as const }
])
</script>

<template>
  <USelect
    :model-value="modelValue"
    :items="items"
    :disabled="disabled"
    icon="i-ph-shield-check"
    size="sm"
    class="w-32"
    @update:model-value="(v: TraceStatusFilter) => emit('update:modelValue', v)"
  />
</template>
