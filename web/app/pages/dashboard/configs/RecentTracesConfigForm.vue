<script setup lang="ts">
import type { RecentTracesConfig, TraceSortMode } from '../types'
import RangePresetSelect from './RangePresetSelect.vue'

const props = defineProps<{
  modelValue: RecentTracesConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: RecentTracesConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<RecentTracesConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

const sortItems = computed(() =>
  (['recent', 'slowest', 'errors-first'] as TraceSortMode[]).map(s => ({
    label: t(`dashboard.config.traceSort.${sortKey(s)}`),
    value: s
  }))
)

function sortKey(s: TraceSortMode): string {
  return s === 'errors-first' ? 'errorsFirst' : s
}
</script>

<template>
  <div class="flex flex-col gap-3 h-full min-h-0">
    <UFormField :label="t('dashboard.config.title')">
      <UInput
        :model-value="modelValue.title ?? ''"
        @update:model-value="(v) => patch({ title: v ? String(v) : undefined })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.range')">
      <RangePresetSelect
        :model-value="modelValue.range"
        @update:model-value="(v) => patch({ range: v })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.service')" :hint="t('dashboard.config.serviceHint')">
      <UInput
        :model-value="modelValue.service ?? ''"
        :placeholder="t('dashboard.config.allServices')"
        @update:model-value="(v) => patch({ service: v ? String(v) : null })"
      />
    </UFormField>

    <div class="grid grid-cols-2 gap-3">
      <UFormField :label="t('dashboard.config.traceSort.label')">
        <USelectMenu
          :model-value="modelValue.sort ?? 'recent'"
          :items="sortItems"
          value-key="value"
          @update:model-value="(v) => patch({ sort: v as TraceSortMode })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.limit')">
        <UInput
          type="number"
          min="1"
          max="200"
          :model-value="modelValue.limit ?? 20"
          @update:model-value="(v) => patch({ limit: clampLimit(v) })"
        />
      </UFormField>
    </div>
  </div>
</template>

<script lang="ts">
function clampLimit(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v)
  if (!Number.isFinite(n)) return 20
  return Math.max(1, Math.min(200, Math.floor(n)))
}
</script>
