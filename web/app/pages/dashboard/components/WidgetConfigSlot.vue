<script setup lang="ts">
import { computed, provide, toRef } from 'vue'
import { resolveConfigForm, useWidgetCatalog } from '../catalog'
import { WIDGET_KIND_INJECTION_KEY } from '../injectionKeys'
import type { FQKind, WidgetConfig } from '../types'

/**
 * Renders the config form matching `kind`, two-way bound to `modelValue`.
 * Looks up the definition through the dynamic catalog so the same slot
 * mounts the right form regardless of source — builtin, custom, library.
 *
 * `kind` is fully-qualified; legacy bare-kind values resolve via the
 * catalog's compat layer.
 */
const props = defineProps<{
  kind: FQKind
  modelValue: WidgetConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: WidgetConfig]
}>()

const catalog = useWidgetCatalog()
const definition = computed(() => catalog.byKind(props.kind))
const component = computed(() => resolveConfigForm(definition.value))

// Engine-specific forms (e.g. HTML template) inject this to look up
// the source definition's spec. Forms that don't care just don't inject.
provide(WIDGET_KIND_INJECTION_KEY, toRef(props, 'kind'))
</script>

<template>
  <component
    :is="component"
    v-if="component"
    :model-value="modelValue"
    @update:model-value="(v: WidgetConfig) => emit('update:modelValue', v)"
  />
</template>
