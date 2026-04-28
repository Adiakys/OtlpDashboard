<script setup lang="ts">
import AppBadge from '~/components/ui/AppBadge.vue'
import type { InstrumentDto } from '~/services/types'

const props = defineProps<{
  instrument: InstrumentDto
  depth: number
  selected: boolean
  disabled: boolean
}>()

const emit = defineEmits<{ toggle: [instrument: InstrumentDto] }>()

const { t } = useI18n()

function onClick() {
  if (props.disabled) return
  emit('toggle', props.instrument)
}

const tooltipLines = computed(() => {
  const lines = [
    `${props.instrument.scopeName || '(root)'} · ${props.instrument.kind}`
  ]
  if (props.instrument.unit) lines.push(`${t('metrics.chart.unit')}: ${props.instrument.unit}`)
  if (props.instrument.description) lines.push(props.instrument.description)
  return lines.join('\n')
})

const ariaLabel = computed(() =>
  props.disabled ? t('metrics.tree.incompatibleKind') : props.instrument.name
)
</script>

<template>
  <button
    type="button"
    class="w-full flex items-center gap-2 pr-2 py-1 rounded-md text-left transition-colors"
    :class="[
      depth > 0 ? '' : '',
      selected ? 'bg-primary/10 text-primary' : 'hover:bg-elevated text-default',
      disabled ? 'opacity-40 cursor-not-allowed hover:bg-transparent' : 'cursor-pointer'
    ]"
    :style="{ paddingLeft: `${0.5 + depth * 1}rem` }"
    :aria-label="ariaLabel"
    :title="tooltipLines"
    :disabled="disabled"
    @click="onClick"
  >
    <span
      class="size-3.5 shrink-0 rounded border flex items-center justify-center transition-colors"
      :class="selected
        ? 'bg-primary border-primary text-white'
        : 'border-default bg-default'"
    >
      <UIcon v-if="selected" name="i-lucide-check" class="size-2.5" />
    </span>
    <span class="truncate font-mono text-xs flex-1 min-w-0">{{ instrument.name }}</span>
    <AppBadge
      :tone="{ kind: 'metric-kind', instrumentKind: instrument.kind }"
      size="xs"
      class="shrink-0"
    >
      {{ instrument.kind }}
    </AppBadge>
  </button>
</template>
