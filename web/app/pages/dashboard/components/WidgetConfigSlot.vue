<script setup lang="ts">
import { computed } from 'vue'
import { WIDGET_REGISTRY } from '../registry'
import type { WidgetConfig, WidgetKind } from '../types'

/**
 * Renders the config form matching `kind`, two-way bound to `modelValue`. A
 * single component replaces the 10-arm `v-if/v-else-if` ladder that used to
 * live in `WidgetConfigDrawer.vue`.
 */
const props = defineProps<{
  kind: WidgetKind
  modelValue: WidgetConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: WidgetConfig]
}>()

const component = computed(() => WIDGET_REGISTRY[props.kind].configForm)
</script>

<template>
  <component
    :is="component"
    :model-value="modelValue"
    @update:model-value="(v: WidgetConfig) => emit('update:modelValue', v)"
  />
</template>
