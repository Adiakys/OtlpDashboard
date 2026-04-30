<script setup lang="ts">
import { formatValue, parseUnitInput, type UnitKind } from '~/lib/units/format'
import type { ThresholdStop } from '~/lib/units/thresholds'

const props = defineProps<{
  modelValue: ThresholdStop[] | undefined
  unitKind: UnitKind | undefined
}>()

const emit = defineEmits<{
  'update:modelValue': [value: ThresholdStop[]]
}>()

const { t } = useI18n()

// Local edit state for values. We keep the raw text so the user can type
// "100 MB" without us round-tripping through a number while they're still
// typing. After a successful commit we re-display the stored value through
// the unit formatter so the input shows "100 MiB" instead of "104857600".
const drafts = ref<string[]>([])
const focused = ref<number | null>(null)

watchEffect(() => {
  const stops = props.modelValue ?? []
  drafts.value = stops.map((t, i) =>
    i === focused.value ? (drafts.value[i] ?? '') : displayFor(t.value, props.unitKind ?? 'none')
  )
})

function displayFor(value: number, kind: UnitKind): string {
  if (kind === 'none') return String(value)
  return formatValue(value, kind, { decimals: 2 })
}

const stops = computed(() => props.modelValue ?? [])
const unit = computed<UnitKind>(() => props.unitKind ?? 'none')

const DEFAULT_COLORS = ['#22c55e', '#eab308', '#ef4444', '#3b82f6', '#a855f7']

function addStop() {
  const next = [...stops.value]
  const lastVal = next.length > 0 ? next[next.length - 1]!.value : 0
  next.push({
    value: Number.isFinite(lastVal) ? lastVal + 1 : 0,
    color: DEFAULT_COLORS[next.length % DEFAULT_COLORS.length]!
  })
  emit('update:modelValue', next)
}

function removeStop(i: number) {
  const next = stops.value.filter((_, idx) => idx !== i)
  emit('update:modelValue', next)
}

function updateColor(i: number, color: string) {
  const next = stops.value.map((s, idx) => (idx === i ? { ...s, color } : s))
  emit('update:modelValue', next)
}

function commitValue(i: number, raw: string) {
  const parsed = parseUnitInput(raw, unit.value)
  if (!Number.isFinite(parsed)) return // keep stale draft, don't overwrite stored
  const next = stops.value.map((s, idx) => (idx === i ? { ...s, value: parsed } : s))
  emit('update:modelValue', next)
}

function onBlur(i: number) {
  commitValue(i, drafts.value[i] ?? '')
  focused.value = null
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div v-if="stops.length === 0" class="text-xs text-muted">
      {{ t('dashboard.config.thresholds.empty') }}
    </div>
    <div
      v-for="(s, i) in stops"
      :key="i"
      class="flex items-center gap-2"
    >
      <input
        type="color"
        :value="s.color"
        class="w-7 h-7 rounded border border-default cursor-pointer p-0"
        @input="(e) => updateColor(i, (e.target as HTMLInputElement).value)"
      >
      <UInput
        v-model="drafts[i]"
        class="flex-1"
        :placeholder="t('dashboard.config.thresholds.valuePlaceholder')"
        @focus="focused = i"
        @blur="onBlur(i)"
        @keydown.enter="onBlur(i)"
      />
      <UButton
        icon="i-ph-trash"
        variant="ghost"
        color="error"
        size="xs"
        :aria-label="t('dashboard.config.thresholds.remove')"
        @click="removeStop(i)"
      />
    </div>
    <UButton
      icon="i-ph-plus"
      variant="soft"
      size="xs"
      class="self-start"
      @click="addStop"
    >
      {{ t('dashboard.config.thresholds.add') }}
    </UButton>
  </div>
</template>
