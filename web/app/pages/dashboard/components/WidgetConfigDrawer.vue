<script setup lang="ts">
import { useWidgetCatalog } from '../catalog'
import { WIDGET_REGISTRY } from '../registry'
import SaveAsTemplateDialog from './SaveAsTemplateDialog.vue'
import WidgetConfigSlot from './WidgetConfigSlot.vue'
import type { BuiltinKind, WidgetConfig, WidgetItem } from '../types'
import { parseKind } from '../types'

const props = defineProps<{
  widget: WidgetItem
  open: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'apply': [config: WidgetConfig]
}>()

const { t } = useI18n()

// Drawer keeps a local draft so the user can tweak fields before pressing
// "Apply" (avoids clobbering layout state on every keystroke; also makes
// "Cancel" trivial — just close without emitting).
//
// JSON round-trip instead of structuredClone: structuredClone trips on Vue's
// reactive Proxies ("Proxy object could not be cloned"), and our config
// shapes are plain data with no Date / Map / etc., so the JSON path is both
// correct and sufficient.
function cloneConfig(c: WidgetConfig): WidgetConfig {
  return JSON.parse(JSON.stringify(c)) as WidgetConfig
}

const draft = ref<WidgetConfig>(cloneConfig(props.widget.config))

watch(() => props.widget.id, () => {
  draft.value = cloneConfig(props.widget.config)
})
watch(() => props.open, isOpen => {
  if (isOpen) draft.value = cloneConfig(props.widget.config)
})

function apply() {
  emit('apply', draft.value)
  emit('update:open', false)
}

function close() {
  emit('update:open', false)
}

// Header label resolution: prefer the catalog (custom widgets carry their
// own display name); fall back to the builtin's i18n title key when the
// definition is a `preset` wrapping a known builtin.
const catalog = useWidgetCatalog()
const definition = computed(() => catalog.byKind(props.widget.kind))

const headerIcon = computed(() => {
  const def = definition.value
  if (def) return def.icon
  // Last-ditch: parse the bare kind to fetch the registry icon directly
  // (covers the case where a stale custom kind is no longer in the catalog).
  const parsed = parseKind(props.widget.kind)
  if (parsed.source === 'std' && parsed.id in WIDGET_REGISTRY) {
    return WIDGET_REGISTRY[parsed.id as BuiltinKind].icon
  }
  return 'i-ph-puzzle-piece'
})

const headerTitle = computed(() => {
  const def = definition.value
  if (def && def.source !== 'std') {
    return def.name
  }
  const parsed = parseKind(props.widget.kind)
  if (parsed.source === 'std' && parsed.id in WIDGET_REGISTRY) {
    return t(WIDGET_REGISTRY[parsed.id as BuiltinKind].titleKey)
  }
  return def?.name ?? props.widget.kind
})

// "Save as my widget" only makes sense when wrapping a builtin (engine
// `preset`, baseKind known). Custom-of-custom is rejected server-side too.
const canSaveAsTemplate = computed(() => {
  const parsed = parseKind(props.widget.kind)
  return parsed.source === 'std' && parsed.id in WIDGET_REGISTRY
})

const saveAsOpen = ref(false)
function openSaveAs() {
  saveAsOpen.value = true
}
</script>

<template>
  <USlideover
    :open="open"
    side="right"
    :title="headerTitle"
    @update:open="(v) => emit('update:open', v)"
  >
    <template #header>
      <div class="flex items-center gap-2">
        <UIcon :name="headerIcon" class="size-4 text-primary" />
        <span class="text-sm font-medium">{{ headerTitle }}</span>
      </div>
    </template>

    <template #body>
      <div class="h-full flex flex-col min-h-0">
        <WidgetConfigSlot
          :kind="widget.kind"
          :model-value="draft"
          @update:model-value="(v: WidgetConfig) => draft = v"
        />
      </div>
    </template>

    <template #footer>
      <div class="flex items-center gap-2 w-full">
        <UButton
          v-if="canSaveAsTemplate"
          color="neutral"
          variant="subtle"
          icon="i-ph-floppy-disk"
          @click="openSaveAs"
        >
          {{ t('widgets.saveAs.action') }}
        </UButton>
        <div class="flex-1" />
        <UButton color="neutral" variant="ghost" @click="close">
          {{ t('dashboard.actions.cancel') }}
        </UButton>
        <UButton color="primary" @click="apply">
          {{ t('dashboard.config.apply') }}
        </UButton>
      </div>
    </template>
  </USlideover>

  <SaveAsTemplateDialog
    v-model:open="saveAsOpen"
    :widget="{ ...widget, config: draft }"
  />
</template>
