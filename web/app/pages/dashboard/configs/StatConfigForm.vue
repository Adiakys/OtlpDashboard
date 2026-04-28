<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import type { MetricBinding, MetricStatConfig } from '../types'
import RangePresetSelect from './RangePresetSelect.vue'

const props = defineProps<{
  modelValue: MetricStatConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricStatConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<MetricStatConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
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

    <div class="grid grid-cols-2 gap-3">
      <UFormField :label="t('dashboard.config.decimals')">
        <UInput
          type="number"
          min="0"
          max="6"
          :model-value="modelValue.decimals ?? 2"
          @update:model-value="(v) => patch({ decimals: clampDecimals(v) })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.unit')">
        <UInput
          :model-value="modelValue.unit ?? ''"
          :placeholder="modelValue.metric?.unit ?? ''"
          @update:model-value="(v) => patch({ unit: v ? String(v) : undefined })"
        />
      </UFormField>
    </div>

    <UFormField>
      <USwitch
        :model-value="modelValue.showSparkline"
        :label="t('dashboard.config.showSparkline')"
        @update:model-value="(v) => patch({ showSparkline: Boolean(v) })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.metric')" class="flex-1 min-h-0">
      <div class="h-64 min-h-0">
        <InstrumentPicker
          mode="single"
          :model-value="modelValue.metric"
          @update:model-value="(v) => patch({ metric: v as MetricBinding | null })"
        />
      </div>
    </UFormField>
  </div>
</template>

<script lang="ts">
function clampDecimals(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v)
  if (!Number.isFinite(n)) return 2
  return Math.max(0, Math.min(6, Math.floor(n)))
}
</script>
