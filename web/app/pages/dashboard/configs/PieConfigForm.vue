<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import type { MetricBinding, MetricPieConfig } from '../types'
import type { CalcMode } from '~/lib/units/calc'
import type { UnitKind } from '~/lib/units/format'
import RangePresetSelect from './RangePresetSelect.vue'
import UnitKindSelect from './UnitKindSelect.vue'
import CalcSelect from './CalcSelect.vue'

const props = defineProps<{
  modelValue: MetricPieConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: MetricPieConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<MetricPieConfig>) {
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

    <UFormField :label="t('dashboard.config.splitBy')" :hint="t('dashboard.config.splitByPieHint')">
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

    <div class="grid grid-cols-2 gap-3">
      <UFormField>
        <USwitch
          :model-value="modelValue.donut === true"
          :label="t('dashboard.config.donut')"
          @update:model-value="(v) => patch({ donut: Boolean(v) })"
        />
      </UFormField>
      <UFormField>
        <USwitch
          :model-value="modelValue.showLegend !== false"
          :label="t('dashboard.config.showLegend')"
          @update:model-value="(v) => patch({ showLegend: Boolean(v) })"
        />
      </UFormField>
    </div>

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
