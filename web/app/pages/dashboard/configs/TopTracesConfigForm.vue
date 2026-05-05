<script setup lang="ts">
import type { TopTracesConfig, TopTracesMetric } from '../types'
import RangePresetSelect from './RangePresetSelect.vue'

const props = defineProps<{
  modelValue: TopTracesConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: TopTracesConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<TopTracesConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

const metricItems = computed(() =>
  (['count', 'errorRate', 'avgMs', 'maxMs'] as TopTracesMetric[]).map(m => ({
    label: t(`dashboard.config.topTracesMetric.${m}`),
    value: m
  }))
)
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
      <UFormField :label="t('dashboard.config.topTracesMetric.label')">
        <USelectMenu
          :model-value="modelValue.metric ?? 'count'"
          :items="metricItems"
          value-key="value"
          @update:model-value="(v) => patch({ metric: v as TopTracesMetric })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.limit')">
        <UInput
          type="number"
          min="1"
          max="100"
          :model-value="modelValue.limit ?? 10"
          @update:model-value="(v) => patch({ limit: clampLimit(v) })"
        />
      </UFormField>
    </div>
  </div>
</template>

<script lang="ts">
function clampLimit(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v)
  if (!Number.isFinite(n)) return 10
  return Math.max(1, Math.min(100, Math.floor(n)))
}
</script>
