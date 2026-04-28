<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import type { MetricBinding, MetricLineConfig } from '../types'
import RangePresetSelect from './RangePresetSelect.vue'

const props = defineProps<{
  modelValue: MetricLineConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricLineConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<MetricLineConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

// Surface attribute keys from the picked metrics so the user can choose a
// split-by axis. We don't have the loaded points here, so the dropdown stays
// purely informational (typed in by hand or chosen from a recent set the
// user already saw on /metrics). Default to "all attributes" otherwise.
const splitByOptions = computed(() => [
  { label: t('dashboard.splitBy.all'), value: '' },
])
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

    <UFormField :label="t('dashboard.config.splitBy')" :hint="t('dashboard.config.splitByHint')">
      <UInput
        :model-value="modelValue.splitBy ?? ''"
        :placeholder="t('dashboard.splitBy.all')"
        @update:model-value="(v) => patch({ splitBy: v ? String(v) : null })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.metrics')" class="flex-1 min-h-0">
      <div class="h-72 min-h-0">
        <InstrumentPicker
          mode="multi"
          :model-value="modelValue.metrics"
          @update:model-value="(v) => patch({ metrics: (v as MetricBinding[] | null) ?? [] })"
        />
      </div>
    </UFormField>
  </div>
</template>
