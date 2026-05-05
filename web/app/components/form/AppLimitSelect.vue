<script setup lang="ts">
const props = defineProps<{
  modelValue: number
  /** Override the auto-derived option set. Rarely needed — the default
   *  list is built from the server-reported `$queryMaxLimit` so the
   *  picker stays in sync with whatever cap the operator configured. */
  options?: number[]
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

const { t } = useI18n()
const { $queryMaxLimit } = useNuxtApp()

// Snap to a "nice" 1/2/5 × 10^n rounding so the picker shows readable
// numbers (250, not 247) regardless of the server's exact cap.
function snapNice(n: number): number {
  if (n < 1) return 1
  const exp = Math.floor(Math.log10(n))
  const base = 10 ** exp
  const mantissa = n / base
  const snapped = mantissa < 1.5 ? 1 : mantissa < 3.5 ? 2 : mantissa < 7.5 ? 5 : 10
  return snapped * base
}

/**
 * Build a 4-option ladder anchored at `max`. Three lower options are
 * placed at log-spaced fractions of the max (1/500, 1/100, 1/10) then
 * snapped to nice round numbers, deduped, and clamped to be strictly
 * below `max`. The result is always `[…, max]`.
 *
 * Examples:
 *   max=25000 → [50, 250, 2500, 25000]
 *   max=10000 → [20, 100, 1000, 10000]
 *   max=1000  → [2, 10, 100, 1000]
 */
function deriveOptions(max: number): number[] {
  if (max <= 1) return [Math.max(1, Math.floor(max))]
  const ratios = [1 / 500, 1 / 100, 1 / 10]
  const lower = ratios
    .map(r => snapNice(max * r))
    .filter(v => v >= 1 && v < max)
  return [...new Set([...lower, max])].sort((a, b) => a - b)
}

// Fallback while /v1/info is in flight or on the unauthenticated leg
// (where the cap isn't surfaced). Keeps the picker functional but with
// modest values — the small max means small page sizes, which is the
// safe direction.
const STATIC_FALLBACK = [25, 50, 100, 500]

const derivedOptions = computed<number[]>(() => {
  if (props.options) return props.options
  return $queryMaxLimit.value ? deriveOptions($queryMaxLimit.value) : STATIC_FALLBACK
})

const items = computed(() => {
  // Keep the current selection visible even if it doesn't fall on a
  // ladder rung — avoids the USelect blank-state when the composable's
  // initial limit (e.g. 50) isn't one of the derived options.
  const base = derivedOptions.value
  const withCurrent = base.includes(props.modelValue)
    ? base
    : [...base, props.modelValue].sort((a, b) => a - b)
  return withCurrent.map(n => ({ label: String(n), value: String(n) }))
})

// Pre-select the penultimate option as soon as the server-side cap
// resolves (or the static fallback is in use). One-shot — after this
// fires, the user owns the selection. We deliberately wait for the
// server cap (`$queryMaxLimit`) instead of firing on the static
// fallback immediately, otherwise an authenticated page would briefly
// snap to the fallback's penultimate (100) before settling on the
// real one.
const hasAutoSelected = ref(false)
watchEffect(() => {
  if (hasAutoSelected.value) return
  if (props.options !== undefined) return
  if (!$queryMaxLimit.value) return
  const list = derivedOptions.value
  if (list.length < 2) return
  const penultimate = list[list.length - 2]!
  if (props.modelValue !== penultimate) {
    emit('update:modelValue', penultimate)
  }
  hasAutoSelected.value = true
})

const selected = computed<string>({
  get: () => String(props.modelValue),
  set: (v: string) => emit('update:modelValue', Number(v))
})
</script>

<template>
  <USelect
    v-model="selected"
    :items="items"
    :disabled="disabled"
    :placeholder="t('filter.limit')"
    icon="i-ph-list-numbers"
    size="sm"
    class="w-24"
  />
</template>
