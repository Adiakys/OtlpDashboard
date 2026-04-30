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
 * If a kind isn't registered (deleted custom widget) or its engine isn't
 * implemented yet (spec/composite from a library, prior to iter 2/5), the
 * slot renders a placeholder rather than crashing — matching the
 * "metric binding not resolvable" UX from `dashboardLayoutIO`.
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

const { t } = useI18n()
const catalog = useWidgetCatalog()
const definition = computed(() => catalog.byKind(props.item.kind))
const component = computed(() => resolveWidgetComponent(definition.value))

/**
 * Placeholder copy when there's no component to mount. Distinguishes
 * "kind not in the catalog" (deleted / unknown) from "kind present but
 * needs an engine the SPA hasn't shipped yet" so the user knows whether
 * to remove the widget or wait for a future release.
 */
const placeholderMessage = computed(() => {
  const def = definition.value
  if (def === null) {
    return t('dashboard.widgets.notAvailable', { kind: props.item.kind })
  }
  if (def.engine === 'spec') {
    return t('dashboard.widgets.engineSpecUnavailable')
  }
  if (def.engine === 'composite') {
    return t('dashboard.widgets.engineCompositeUnavailable')
  }
  return t('dashboard.widgets.notAvailable', { kind: props.item.kind })
})
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
    {{ placeholderMessage }}
  </div>
</template>
