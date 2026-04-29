<script setup lang="ts">
import { UNIT_KINDS, type UnitKind } from '~/lib/units/format'

defineProps<{
  modelValue: UnitKind | undefined
}>()

defineEmits<{
  'update:modelValue': [value: UnitKind]
}>()

const { t } = useI18n()

const items = computed(() =>
  UNIT_KINDS.map(k => ({
    label: t(`dashboard.config.unitKind.${unitKey(k)}`),
    value: k
  }))
)

function unitKey(k: UnitKind): string {
  // Keep i18n keys plain camelCase — kebab and slashes don't survive nested
  // JSON dot-paths cleanly.
  switch (k) {
    case 'percent-unit': return 'percentUnit'
    default: return k
  }
}
</script>

<template>
  <USelectMenu
    :model-value="modelValue ?? 'none'"
    :items="items"
    value-key="value"
    @update:model-value="(v) => $emit('update:modelValue', v as UnitKind)"
  />
</template>
