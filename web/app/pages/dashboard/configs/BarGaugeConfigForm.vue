<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import type { MetricBarGaugeConfig, MetricBinding, ThresholdStop } from '../types'
import type { CalcMode } from '~/lib/units/calc'
import type { UnitKind } from '~/lib/units/format'
import RangePresetSelect from './RangePresetSelect.vue'
import UnitKindSelect from './UnitKindSelect.vue'
import CalcSelect from './CalcSelect.vue'
import ThresholdsEditor from './ThresholdsEditor.vue'

const props = defineProps<{
  modelValue: MetricBarGaugeConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricBarGaugeConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<MetricBarGaugeConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

function asNumber(v: unknown, fallback: number): number {
  const n = typeof v === 'number' ? v : Number(v)
  return Number.isFinite(n) ? n : fallback
}

function asNullableNumber(v: unknown): number | null {
  if (v === '' || v === null || v === undefined) return null
  const n = typeof v === 'number' ? v : Number(v)
  return Number.isFinite(n) ? n : null
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

    <UFormField :label="t('dashboard.config.splitBy')" :hint="t('dashboard.config.splitByHint')">
      <UInput
        :model-value="modelValue.splitBy ?? ''"
        :placeholder="t('dashboard.splitBy.all')"
        @update:model-value="(v) => patch({ splitBy: v ? String(v) : null })"
      />
    </UFormField>

    <div class="grid grid-cols-2 gap-3">
      <UFormField :label="t('dashboard.config.calc.label')">
        <CalcSelect
          :model-value="modelValue.calc"
          @update:model-value="(v: CalcMode) => patch({ calc: v })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.unitKind.label')">
        <UnitKindSelect
          :model-value="modelValue.unitKind"
          @update:model-value="(v: UnitKind) => patch({ unitKind: v })"
        />
      </UFormField>
    </div>

    <div class="grid grid-cols-3 gap-3">
      <UFormField :label="t('dashboard.config.topN')">
        <UInput
          type="number"
          min="1"
          max="50"
          :model-value="modelValue.topN ?? 10"
          @update:model-value="(v) => patch({ topN: Math.max(1, Math.min(50, asNumber(v, 10))) })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.min')">
        <UInput
          type="number"
          :model-value="modelValue.min ?? 0"
          @update:model-value="(v) => patch({ min: asNumber(v, 0) })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.max')" :hint="t('dashboard.config.maxAutoHint')">
        <UInput
          type="number"
          :model-value="modelValue.max == null ? '' : String(modelValue.max)"
          :placeholder="t('dashboard.config.maxAuto')"
          @update:model-value="(v) => patch({ max: asNullableNumber(v) })"
        />
      </UFormField>
    </div>

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
