<script setup lang="ts">
import { CALC_MODES, type CalcMode } from '~/lib/units/calc'

defineProps<{
  modelValue: CalcMode | undefined
}>()

defineEmits<{
  'update:modelValue': [value: CalcMode]
}>()

const { t } = useI18n()

const items = computed(() =>
  CALC_MODES.map(c => ({
    label: t(`dashboard.config.calc.${c}`),
    value: c
  }))
)
</script>

<template>
  <USelectMenu
    :model-value="modelValue ?? 'last'"
    :items="items"
    value-key="value"
    @update:model-value="(v) => $emit('update:modelValue', v as CalcMode)"
  />
</template>
