<script setup lang="ts">
import { WIDGET_KINDS, WIDGET_REGISTRY } from '../registry'
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
      <!--
        3 columns from sm onwards keeps the modal compact even with 10+ kinds;
        on narrow viewports we fall back to 2 columns so labels don't wrap.
        Each card is keyboard-focusable with a visible primary ring.
      -->
      <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
        <button
          v-for="kind in WIDGET_KINDS"
          :key="kind"
          type="button"
          class="flex flex-col gap-2 p-3 border border-default rounded-lg text-left transition-colors hover:border-primary hover:bg-elevated/40 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:border-primary"
          @click="pick(kind)"
        >
          <div class="flex items-center gap-2">
            <UIcon :name="WIDGET_REGISTRY[kind].icon" class="size-5 text-primary shrink-0" />
            <span class="font-medium text-sm truncate">{{ t(WIDGET_REGISTRY[kind].titleKey) }}</span>
          </div>
          <p class="text-xs text-muted leading-snug line-clamp-2">
            {{ t(WIDGET_REGISTRY[kind].descKey) }}
          </p>
        </button>
      </div>
    </template>
  </UModal>
</template>
