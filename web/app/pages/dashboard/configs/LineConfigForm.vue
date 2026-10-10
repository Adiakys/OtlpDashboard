<script setup lang="ts">
import InstrumentPicker from '../components/InstrumentPicker.vue'
import ParametersSection from '../components/ParametersSection.vue'
import type { MetricBinding, MetricLineConfig } from '../types'
import type { UnitKind } from '~/lib/units/format'
import type { ChartType } from '~/lib/agcharts/chartStrategy'
import RangePresetSelect from './RangePresetSelect.vue'
import UnitKindSelect from './UnitKindSelect.vue'

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

// `'auto'` is a synthetic option that maps to `chartTypeOverride: undefined`
// so the widget falls back to `pickChartType()` based on instrument metadata.
const CHART_TYPE_OPTIONS = ['auto', 'line', 'area', 'column'] as const
type ChartTypeOption = typeof CHART_TYPE_OPTIONS[number]

const chartTypeItems = computed(() =>
  CHART_TYPE_OPTIONS.map(o => ({
    label: t(`dashboard.config.chartType.${o}`),
    value: o
  }))
)

const currentChartType = computed<ChartTypeOption>(() => {
  const v = props.modelValue.chartTypeOverride
  if (!v || v === 'unsupported') return 'auto'
  return v as ChartTypeOption
})

function setLegend(index: number, value: string) {
  const metrics = props.modelValue.metrics.map((m, i) => i === index ? { ...m, legend: value || undefined } : m)
  patch({ metrics })
}

function setChartType(v: ChartTypeOption) {
  if (v === 'auto') {
    patch({ chartTypeOverride: undefined })
  } else {
    patch({ chartTypeOverride: v as ChartType })
  }
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

    <div class="grid grid-cols-3 gap-3">
      <UFormField :label="t('dashboard.config.chartType.label')">
        <USelectMenu
          :model-value="currentChartType"
          :items="chartTypeItems"
          value-key="value"
          @update:model-value="(v) => setChartType(v as ChartTypeOption)"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.unitKind.label')">
        <UnitKindSelect
          :model-value="modelValue.unitKind"
          @update:model-value="(v: UnitKind) => patch({ unitKind: v })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.decimals')">
        <UInput
          type="number"
          min="0"
          max="6"
          :model-value="modelValue.decimals ?? 2"
          @update:model-value="(v) => patch({ decimals: clampDecimals(v) })"
        />
      </UFormField>
    </div>

    <UFormField :label="t('dashboard.config.splitBy')" :hint="t('dashboard.config.splitByHint')">
      <UInput
        :model-value="modelValue.splitBy ?? ''"
        :placeholder="t('dashboard.splitBy.all')"
        @update:model-value="(v) => patch({ splitBy: v ? String(v) : null })"
      />
    </UFormField>

    <UFormField :label="t('dashboard.config.metrics')" class="shrink-0">
      <div class="h-72 min-h-0">
        <InstrumentPicker
          mode="multi"
          :model-value="modelValue.metrics"
          @update:model-value="(v) => patch({ metrics: (v as MetricBinding[] | null) ?? [] })"
        />
      </div>
    </UFormField>

    <UFormField
      v-if="modelValue.metrics.length > 0"
      :label="t('dashboard.config.legend')"
      :description="t('dashboard.config.legendHint')"
      class="shrink-0"
    >
      <div class="flex flex-col gap-2">
        <div v-for="(m, i) in modelValue.metrics" :key="i" class="flex flex-col gap-1">
          <span class="truncate font-mono text-[11px] text-muted" :title="m.instrumentName">
            {{ m.instrumentName }}<template v-if="m.serviceName"> · {{ m.serviceName }}</template><template v-if="m.serviceInstanceId"> / {{ m.serviceInstanceId }}</template>
          </span>
          <UInput
            size="sm"
            :model-value="m.legend ?? ''"
            :placeholder="t('dashboard.config.legendPlaceholder')"
            @update:model-value="(v) => setLegend(i, String(v ?? ''))"
          />
        </div>
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
