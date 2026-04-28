<script setup lang="ts">
import type { TextWidgetConfig } from '../types'

const props = defineProps<{
  modelValue: TextWidgetConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: TextWidgetConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<TextWidgetConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

const alignItems = computed(() => [
  { label: t('dashboard.config.align.left'), value: 'left' as const },
  { label: t('dashboard.config.align.center'), value: 'center' as const }
])
</script>

<template>
  <div class="flex flex-col gap-3">
    <UFormField :label="t('dashboard.config.title')">
      <UInput
        :model-value="modelValue.title ?? ''"
        @update:model-value="(v) => patch({ title: v ? String(v) : undefined })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.markdown')">
      <UTextarea
        :model-value="modelValue.markdown"
        :rows="10"
        :placeholder="t('dashboard.config.markdownPlaceholder')"
        class="font-mono text-sm"
        @update:model-value="(v) => patch({ markdown: String(v) })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.align.label')">
      <USelectMenu
        :model-value="modelValue.align ?? 'left'"
        :items="alignItems"
        value-key="value"
        @update:model-value="(v) => patch({ align: (v as 'left' | 'center') })"
      />
    </UFormField>
  </div>
</template>
