<script setup lang="ts">
import { computed } from 'vue'
import { WIDGET_REGISTRY } from '../registry'
import type { WidgetItem } from '../types'

/**
 * Renders the widget component matching `item.kind`, hands it `config` plus
 * the standard widget props, and forwards edit/remove. Centralizes the
 * kind→component dispatch so the dashboard grid stays declarative and so
 * adding a new widget kind requires touching only the registry.
 */
const props = defineProps<{
  item: WidgetItem
  isEditing: boolean
  liveTick: number
}>()

const emit = defineEmits<{
  edit: []
  remove: []
}>()

const component = computed(() => WIDGET_REGISTRY[props.item.kind].component)
</script>

<template>
  <component
    :is="component"
    :config="item.config"
    :is-editing="isEditing"
    :live-tick="liveTick"
    @edit="emit('edit')"
    @remove="emit('remove')"
  />
</template>
