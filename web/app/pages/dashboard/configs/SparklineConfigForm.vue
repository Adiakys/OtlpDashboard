<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import type { MetricBinding, MetricSparklineConfig } from '../types'
import RangePresetSelect from './RangePresetSelect.vue'

const props = defineProps<{
  modelValue: MetricSparklineConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricSparklineConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<MetricSparklineConfig>) {
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
