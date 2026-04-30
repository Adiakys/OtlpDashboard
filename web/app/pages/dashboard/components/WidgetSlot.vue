<script setup lang="ts">
import { computed } from 'vue'
import { resolveComponent as resolveWidgetComponent, useWidgetCatalog } from '../catalog'
import type { WidgetItem } from '../types'

/**
 * Renders the widget component matching `item.kind`, hands it `config` plus
 * the standard widget props, and forwards edit/remove. Looks up the
 * definition through the dynamic catalog so the same slot handles builtin,
 * custom, and library-sourced widgets uniformly.
 *
 * If a kind isn't registered (e.g. a deleted custom widget still referenced
 * by an existing dashboard), the slot renders a placeholder rather than
 * crashing — matching the "metric binding not resolvable" UX already used
 * by `dashboardLayoutIO`.
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

const catalog = useWidgetCatalog()
const definition = computed(() => catalog.byKind(props.item.kind))
const component = computed(() => resolveWidgetComponent(definition.value))
</script>

<template>
  <component
    :is="component"
    v-if="component"
    :config="item.config"
    :is-editing="isEditing"
    :live-tick="liveTick"
    @edit="emit('edit')"
    @remove="emit('remove')"
  />
  <div
    v-else
    class="size-full flex items-center justify-center px-4 text-center text-overline"
    style="color: var(--color-graphite-500);"
  >
    {{ $t('dashboard.widgets.notAvailable', { kind: item.kind }) }}
  </div>
</template>
