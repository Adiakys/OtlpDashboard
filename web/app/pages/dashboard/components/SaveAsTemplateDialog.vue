<script setup lang="ts">
import { ref, watch } from 'vue'
import { useWidgetCatalog } from '../catalog'
import { WIDGET_REGISTRY } from '../registry'
import type { BuiltinKind, WidgetItem } from '../types'
import { parseKind } from '../types'
import type { SaveWidgetDefinitionRequest } from '~/services/types'

/**
 * Modal that asks the user to name a widget definition (preset wrapping the
 * currently-edited builtin) and persists it via the WidgetService. The new
 * custom widget shows up in the picker on the next open thanks to the
 * catalog refresh wired here.
 */
const props = defineProps<{
  open: boolean
  widget: WidgetItem
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  saved: []
}>()

const { t } = useI18n()
const { $widgetService } = useNuxtApp()
const catalog = useWidgetCatalog()

const baseKind = computed<BuiltinKind | null>(() => {
  const parsed = parseKind(props.widget.kind)
  if (parsed.source !== 'std') return null
  if (parsed.id in WIDGET_REGISTRY) return parsed.id as BuiltinKind
  return null
})

const name = ref('')
const description = ref('')
const icon = ref('i-ph-puzzle-piece')
const defaultW = ref(3)
const defaultH = ref(3)
const isSaving = ref(false)
const error = ref<string | null>(null)

watch(() => props.open, isOpen => {
  if (!isOpen) return
  // Reset draft when re-opening; pre-populate icon + defaultSize from the
  // builtin we're wrapping so the user only has to type a name in the
  // common case.
  const bk = baseKind.value
  const meta = bk ? WIDGET_REGISTRY[bk] : null
  name.value = ''
  description.value = ''
  icon.value = meta?.icon ?? 'i-ph-puzzle-piece'
  defaultW.value = meta?.defaultSize.w ?? 3
  defaultH.value = meta?.defaultSize.h ?? 3
  error.value = null
})

async function submit() {
  if (isSaving.value) return
  if (!baseKind.value) {
    error.value = t('widgets.saveAs.notSupported')
    return
  }
  if (!name.value.trim()) {
    error.value = t('widgets.saveAs.nameRequired')
    return
  }

  isSaving.value = true
  error.value = null
  try {
    const request: SaveWidgetDefinitionRequest = {
      name: name.value.trim(),
      description: description.value.trim() || null,
      icon: icon.value.trim(),
      engine: 'Preset',
      baseKind: baseKind.value,
      // Snapshot the current per-instance config as the new template's seed.
      // JSON round-trip strips reactivity proxies and matches what the
      // server will store.
      config: JSON.parse(JSON.stringify(props.widget.config)),
      spec: null,
      defaultW: defaultW.value,
      defaultH: defaultH.value,
      rowVersion: 0
    }
    await $widgetService.createCustom(request)
    await catalog.refreshCustom()
    emit('saved')
    emit('update:open', false)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  } finally {
    isSaving.value = false
  }
}

function close() {
  if (isSaving.value) return
  emit('update:open', false)
}
</script>

<template>
  <UModal
    :open="open"
    :title="t('widgets.saveAs.title')"
    @update:open="(v) => emit('update:open', v)"
  >
    <template #body>
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <div v-if="!baseKind" class="text-mono-sm" style="color: var(--color-rust-700);">
          {{ t('widgets.saveAs.notSupported') }}
        </div>

        <label class="flex flex-col gap-1.5">
          <span class="text-overline" style="color: var(--color-graphite-500);">
            {{ t('widgets.saveAs.name') }}
          </span>
          <UInput
            v-model="name"
            size="sm"
            :placeholder="t('widgets.saveAs.namePlaceholder')"
            :disabled="isSaving"
            autofocus
            required
          />
        </label>

        <label class="flex flex-col gap-1.5">
          <span class="text-overline" style="color: var(--color-graphite-500);">
            {{ t('widgets.saveAs.description') }}
          </span>
          <UInput
            v-model="description"
            size="sm"
            :placeholder="t('widgets.saveAs.descriptionPlaceholder')"
            :disabled="isSaving"
          />
        </label>

        <label class="flex flex-col gap-1.5">
          <span class="text-overline" style="color: var(--color-graphite-500);">
            {{ t('widgets.saveAs.icon') }}
          </span>
          <div class="flex items-center gap-2">
            <UIcon :name="icon" class="size-4 shrink-0" style="color: var(--color-ember-500);" />
            <UInput
              v-model="icon"
              size="sm"
              class="flex-1"
              :placeholder="'i-ph-…'"
              :disabled="isSaving"
            />
          </div>
          <span class="text-mono-sm" style="color: var(--color-graphite-500);">
            {{ t('widgets.saveAs.iconHint') }}
          </span>
        </label>

        <div class="grid grid-cols-2 gap-3">
          <label class="flex flex-col gap-1.5">
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ t('widgets.saveAs.defaultW') }}
            </span>
            <UInput
              v-model.number="defaultW"
              type="number"
              size="sm"
              :min="1"
              :max="12"
              :disabled="isSaving"
            />
          </label>
          <label class="flex flex-col gap-1.5">
            <span class="text-overline" style="color: var(--color-graphite-500);">
              {{ t('widgets.saveAs.defaultH') }}
            </span>
            <UInput
              v-model.number="defaultH"
              type="number"
              size="sm"
              :min="1"
              :max="24"
              :disabled="isSaving"
            />
          </label>
        </div>

        <p
          v-if="error"
          role="alert"
          class="text-mono-sm"
          style="color: var(--color-rust-700);"
        >
          {{ error }}
        </p>
      </form>
    </template>

    <template #footer>
      <div class="flex items-center justify-end gap-2 w-full">
        <UButton color="neutral" variant="ghost" :disabled="isSaving" @click="close">
          {{ t('dashboard.actions.cancel') }}
        </UButton>
        <UButton color="primary" :loading="isSaving" :disabled="!baseKind || !name.trim()" @click="submit">
          {{ t('widgets.saveAs.submit') }}
        </UButton>
      </div>
    </template>
  </UModal>
</template>
