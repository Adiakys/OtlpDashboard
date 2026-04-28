<script setup lang="ts">
import AppBadge from '~/components/ui/AppBadge.vue'
import MetricSplitBySelect from './MetricSplitBySelect.vue'
import type { InstrumentDto } from '~/services/types'
import type { SplitBy } from '~/lib/agcharts/seriesGrouping'
import { instrumentKey } from '../buildTree'

const props = defineProps<{
  selected: InstrumentDto[]
  unit: string | null
  units: (string | null)[]
  splitBy: SplitBy
  availableAttributes: string[]
}>()

const emit = defineEmits<{
  'update:splitBy': [value: SplitBy]
  remove: [key: string]
  'clear-all': []
}>()

const { t } = useI18n()

const unitsLabel = computed(() => {
  const labels = props.units.map(u => u ?? '—')
  return labels.join(', ')
})

const hasMultipleUnits = computed(() => props.units.length > 1)
</script>

<template>
  <div class="px-3 py-2 border border-default rounded-t-lg bg-elevated/40 flex flex-col gap-2">
    <div class="flex flex-wrap items-center gap-1.5">
      <TransitionGroup
        tag="div"
        class="flex flex-wrap gap-1.5 flex-1 min-w-0"
        enter-active-class="transition-all duration-150 ease-out"
        leave-active-class="transition-all duration-150 ease-out"
        enter-from-class="opacity-0 scale-90"
        leave-to-class="opacity-0 scale-90"
      >
        <span
          v-for="i in selected"
          :key="instrumentKey(i)"
          class="inline-flex items-center gap-1.5 pl-2 pr-1 py-0.5 rounded-md border border-default bg-default text-xs"
        >
          <AppBadge
            :tone="{ kind: 'metric-kind', instrumentKind: i.kind }"
            size="xs"
          >
            {{ i.kind }}
          </AppBadge>
          <span class="font-mono truncate max-w-[18rem]" :title="i.name">{{ i.name }}</span>
          <button
            type="button"
            class="size-5 inline-flex items-center justify-center rounded text-muted hover:bg-elevated hover:text-default transition-colors"
            :aria-label="t('common.clear')"
            @click="emit('remove', instrumentKey(i))"
          >
            <UIcon name="i-lucide-x" class="size-3.5" />
          </button>
        </span>
      </TransitionGroup>
      <button
        v-if="selected.length > 1"
        type="button"
        class="shrink-0 text-xs text-muted hover:text-default transition-colors px-2 py-1 rounded hover:bg-elevated"
        @click="emit('clear-all')"
      >
        {{ t('common.clear') }}
      </button>
    </div>

    <div v-if="selected.length > 0" class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex items-center gap-3 text-xs text-muted">
        <span v-if="units.length > 0" class="inline-flex items-center gap-1">
          <UIcon name="i-lucide-ruler" class="size-3.5" />
          <span>{{ hasMultipleUnits ? t('metrics.chart.units') : t('metrics.chart.unit') }}:</span>
          <span class="font-mono text-default">{{ unitsLabel }}</span>
        </span>
      </div>
      <MetricSplitBySelect
        :model-value="splitBy"
        :available="availableAttributes"
        @update:model-value="(v) => emit('update:splitBy', v)"
      />
    </div>
  </div>
</template>
