<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import ParametersSection from '../components/ParametersSection.vue'
import type { MetricBinding, MetricHeatmapConfig, ThresholdStop } from '../types'
import type { CalcMode } from '~/lib/units/calc'
import type { UnitKind } from '~/lib/units/format'
import RangePresetSelect from './RangePresetSelect.vue'
import UnitKindSelect from './UnitKindSelect.vue'
import CalcSelect from './CalcSelect.vue'
import ThresholdsEditor from './ThresholdsEditor.vue'

const props = defineProps<{
  modelValue: MetricHeatmapConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricHeatmapConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<MetricHeatmapConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

function clampBuckets(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v)
  if (!Number.isFinite(n)) return 24
  return Math.max(4, Math.min(120, Math.floor(n)))
}

function clampDecimals(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v)
  if (!Number.isFinite(n)) return 2
  return Math.max(0, Math.min(6, Math.floor(n)))
}
</script>

<template>
  <div class="flex flex-col gap-3 h-full min-h-0">
    <ParametersSection
      :model-value="modelValue.parameters"
      @update:model-value="(v) => patch({ parameters: v })"
    />

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

    <div class="grid grid-cols-3 gap-3">
      <UFormField :label="t('dashboard.config.buckets')">
        <UInput
          type="number"
          min="4"
          max="120"
          :model-value="modelValue.buckets ?? 24"
          @update:model-value="(v) => patch({ buckets: clampBuckets(v) })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.bucketReduce')">
        <CalcSelect
          :model-value="modelValue.bucketReduce"
          @update:model-value="(v: CalcMode) => patch({ bucketReduce: v })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.unitKind.label')">
        <UnitKindSelect
          :model-value="modelValue.unitKind"
          @update:model-value="(v: UnitKind) => patch({ unitKind: v })"
        />
      </UFormField>
    </div>

    <UFormField :label="t('dashboard.config.decimals')">
      <UInput
        type="number"
        min="0"
        max="6"
        :model-value="modelValue.decimals ?? 2"
        @update:model-value="(v) => patch({ decimals: clampDecimals(v) })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.thresholds.label')">
      <ThresholdsEditor
        :model-value="modelValue.thresholds"
        :unit-kind="modelValue.unitKind"
        @update:model-value="(v: ThresholdStop[]) => patch({ thresholds: v })"
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
