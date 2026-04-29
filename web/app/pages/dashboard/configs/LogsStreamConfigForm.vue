<script setup lang="ts">
import type { LogSeverityFilter, LogsStreamConfig } from '../types'
import RangePresetSelect from './RangePresetSelect.vue'

const props = defineProps<{
  modelValue: LogsStreamConfig
}>()

const emit = defineEmits<{
  'update:modelValue': [value: LogsStreamConfig]
}>()

const { t } = useI18n()

function patch(p: Partial<LogsStreamConfig>) {
  emit('update:modelValue', { ...props.modelValue, ...p })
}

const SEVERITIES: LogSeverityFilter[] = ['all', 'info', 'warn', 'error', 'fatal']

const severityItems = computed(() =>
  SEVERITIES.map(s => ({
    label: t(`dashboard.config.severity.${s}`),
    value: s
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
      <UFormField :label="t('dashboard.config.severity.label')">
        <USelectMenu
          :model-value="modelValue.minSeverity ?? 'all'"
          :items="severityItems"
          value-key="value"
          @update:model-value="(v) => patch({ minSeverity: v as LogSeverityFilter })"
        />
      </UFormField>
      <UFormField :label="t('dashboard.config.limit')">
        <UInput
          type="number"
          min="1"
          max="500"
          :model-value="modelValue.limit ?? 50"
          @update:model-value="(v) => patch({ limit: clampLimit(v) })"
        />
      </UFormField>
    </div>
  </div>
</template>

<script lang="ts">
function clampLimit(v: unknown): number {
  const n = typeof v === 'number' ? v : Number(v)
  if (!Number.isFinite(n)) return 50
  return Math.max(1, Math.min(500, Math.floor(n)))
}
</script>
