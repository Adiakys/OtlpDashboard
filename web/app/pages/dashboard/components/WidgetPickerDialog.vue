<script setup lang="ts">
import { WIDGET_KINDS, WIDGET_METADATA } from '../registry'
import type { WidgetKind } from '../types'

defineProps<{
  open: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  select: [kind: WidgetKind]
}>()

const { t } = useI18n()

function pick(kind: WidgetKind) {
  emit('select', kind)
}
</script>

<template>
  <UModal
    :open="open"
    :title="t('dashboard.picker.title')"
    @update:open="(v) => emit('update:open', v)"
  >
    <template #body>
      <div class="grid grid-cols-2 gap-3">
        <button
          v-for="kind in WIDGET_KINDS"
          :key="kind"
          type="button"
          class="flex flex-col gap-2 p-4 border border-default rounded-lg text-left hover:border-primary hover:bg-elevated/40 transition-colors"
          @click="pick(kind)"
        >
          <div class="flex items-center gap-2">
            <UIcon :name="WIDGET_METADATA[kind].icon" class="size-5 text-primary" />
            <span class="font-medium text-sm">{{ t(WIDGET_METADATA[kind].titleKey) }}</span>
          </div>
          <p class="text-xs text-muted leading-snug">
            {{ t(WIDGET_METADATA[kind].descKey) }}
          </p>
        </button>
      </div>
    </template>
  </UModal>
</template>
