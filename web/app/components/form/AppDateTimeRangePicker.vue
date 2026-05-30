<script setup lang="ts">
import type { TimeWindow } from '~/services/types'
import {
  TIME_RANGE_PRESET_MINUTES,
  isKnownPresetKey,
  presetToWindow
} from '~/lib/timeRangePresets'

interface Preset {
  key: string
  labelKey: string
  minutes: number
}

const presets: Preset[] = [
  { key: '5m', labelKey: 'filter.range5m', minutes: TIME_RANGE_PRESET_MINUTES['5m']! },
  { key: '15m', labelKey: 'filter.range15m', minutes: TIME_RANGE_PRESET_MINUTES['15m']! },
  { key: '1h', labelKey: 'filter.range1h', minutes: TIME_RANGE_PRESET_MINUTES['1h']! },
  { key: '6h', labelKey: 'filter.range6h', minutes: TIME_RANGE_PRESET_MINUTES['6h']! },
  { key: '24h', labelKey: 'filter.range24h', minutes: TIME_RANGE_PRESET_MINUTES['24h']! },
  { key: '7d', labelKey: 'filter.range7d', minutes: TIME_RANGE_PRESET_MINUTES['7d']! }
]

const props = defineProps<{
  modelValue: TimeWindow
  /** Active rolling preset, when one is in effect. When set, the picker
   *  highlights the matching button and displays its label as the summary
   *  — independent of where `modelValue` happens to sit (it may drift
   *  forward of "now" because the parent recomputed for a live tick).
   *  `null` means "custom window" (or unknown to this picker). Callers
   *  that don't pass the prop fall back to the legacy heuristic that
   *  infers a preset from `modelValue` only when `to ≈ now`. */
  preset?: string | null
  disabled?: boolean
  /** Server retention for the kind of data this picker filters. When set
   *  and > 0, the popover renders an info icon top-right with a tooltip
   *  reminding the user that older data has been auto-deleted. Null /
   *  zero contributes nothing to the tooltip. */
  retentionDays?: number | null
  /** Maximum query window (in hours, as the server reports it). The
   *  picker converts to days internally before composing the tooltip
   *  — keeping the unit mismatch contained here means the three feature
   *  pages can pass `$queryMaxWindowHours` straight through without
   *  each repeating the `/24` rounding. */
  maxWindowHours?: number | null
}>()

const emit = defineEmits<{
  'update:modelValue': [value: TimeWindow]
  /** Emitted alongside `update:modelValue`. A preset key (e.g. `'1h'`)
   *  when the user clicked a quick-pick, `null` when they applied a
   *  custom from/to. Pages persist this in the URL as `?range=1h` so
   *  the rolling semantic survives back-navigation. */
  'update:preset': [value: string | null]
}>()

const { t, locale } = useI18n()
const isOpen = ref(false)
const draftFrom = ref('')
const draftTo = ref('')

function isoToLocalInput(iso: string): string {
  const d = new Date(iso)
  const tz = d.getTimezoneOffset() * 60_000
  return new Date(d.getTime() - tz).toISOString().slice(0, 16)
}

function localInputToIso(value: string): string {
  return new Date(value).toISOString()
}

function syncDrafts() {
  draftFrom.value = isoToLocalInput(props.modelValue.from)
  draftTo.value = isoToLocalInput(props.modelValue.to)
}

watch(() => [props.modelValue.from, props.modelValue.to], syncDrafts, { immediate: true })

function applyPreset(p: Preset) {
  emit('update:preset', p.key)
  emit('update:modelValue', presetToWindow(p.key as keyof typeof TIME_RANGE_PRESET_MINUTES))
  isOpen.value = false
}

function applyDrafts() {
  if (!draftFrom.value || !draftTo.value) return
  emit('update:preset', null)
  emit('update:modelValue', {
    from: localInputToIso(draftFrom.value),
    to: localInputToIso(draftTo.value)
  })
  isOpen.value = false
}

// When the parent passes `preset`, trust it and skip the from/to-based
// inference (the parent's recomputed window may drift forward of "now"
// between live ticks, breaking the `closeToNow` heuristic).
const matchedPreset = computed<Preset | null>(() => {
  if (isKnownPresetKey(props.preset)) {
    return presets.find(p => p.key === props.preset) ?? null
  }
  if (props.preset === null) return null
  // Legacy path for callers that haven't wired `preset` yet
  // (service-map, metrics): infer when `to ≈ now` and span matches.
  const from = new Date(props.modelValue.from).getTime()
  const to = new Date(props.modelValue.to).getTime()
  const span = to - from
  const closeToNow = Math.abs(Date.now() - to) < 90_000
  if (!closeToNow) return null
  for (const p of presets) {
    const target = p.minutes * 60_000
    if (Math.abs(span - target) < 60_000) return p
  }
  return null
})

const formatter = computed(() => new Intl.DateTimeFormat(locale.value, {
  dateStyle: 'short',
  timeStyle: 'short'
}))

const summary = computed(() => {
  if (matchedPreset.value) return t(matchedPreset.value.labelKey)
  const f = new Date(props.modelValue.from)
  const ttt = new Date(props.modelValue.to)
  return `${formatter.value.format(f)} → ${formatter.value.format(ttt)}`
})

// Two-line info-icon tooltip (retention + max-query-window). Each line
// shows only when its source value is positive — so the
// unauthenticated leg (both null) hides the icon entirely. The
// hours→days conversion lives here on purpose: pages pass the raw
// `$queryMaxWindowHours`, and rounding happens once.
const retentionLine = computed(() =>
  props.retentionDays && props.retentionDays > 0
    ? t('filter.retentionInfo', { days: props.retentionDays })
    : null
)
const maxWindowLine = computed(() => {
  const h = props.maxWindowHours
  if (!h || h <= 0) return null
  return t('filter.maxWindowInfo', { days: Math.round(h / 24) })
})
const showInfoIcon = computed(() =>
  retentionLine.value !== null || maxWindowLine.value !== null
)
</script>

<template>
  <UPopover v-model:open="isOpen">
    <button
      type="button"
      class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      :disabled="disabled"
    >
      <UIcon name="i-ph-clock-countdown" class="size-4 text-muted" />
      <span class="truncate max-w-[18rem]">{{ summary }}</span>
      <UIcon name="i-ph-caret-down" class="size-3.5 text-muted" />
    </button>

    <template #content>
      <div class="relative flex w-[420px] max-w-[92vw]">
        <!-- Server-config hint. Composed from retention + max-query-
             window (both auth-gated). The icon hides when neither piece
             is set. The default Nuxt UI tooltip theme is sized for
             single-line strings (`h-6` + `truncate`), so we use the
             `#content` slot (sidesteps `truncate`) and override the
             bubble's fixed height + vertical-center via `:ui` so the
             two-line content can grow naturally. -->
        <UTooltip
          v-if="showInfoIcon"
          :ui="{ content: 'h-auto !items-start py-1.5 max-w-xs' }"
        >
          <UIcon
            name="i-ph-info"
            class="absolute top-2 right-2 size-4 text-muted hover:text-default cursor-help z-10"
          />
          <template #content>
            <div class="flex flex-col gap-1 text-xs leading-snug">
              <p v-if="retentionLine">{{ retentionLine }}</p>
              <p v-if="maxWindowLine">{{ maxWindowLine }}</p>
            </div>
          </template>
        </UTooltip>

        <div class="w-32 shrink-0 border-r border-default p-2 space-y-0.5">
          <button
            v-for="p in presets"
            :key="p.key"
            type="button"
            class="vellum-preset"
            :class="matchedPreset?.key === p.key ? 'vellum-preset--active' : ''"
            @click="applyPreset(p)"
          >
            {{ t(p.labelKey) }}
          </button>
        </div>
        <div class="flex-1 p-3 space-y-3">
          <label class="block">
            <span class="text-xs text-muted">{{ t('common.from') }}</span>
            <input
              v-model="draftFrom"
              type="datetime-local"
              class="mt-1 w-full px-2 py-1.5 rounded-md border border-default bg-default text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
          </label>
          <label class="block">
            <span class="text-xs text-muted">{{ t('common.to') }}</span>
            <input
              v-model="draftTo"
              type="datetime-local"
              class="mt-1 w-full px-2 py-1.5 rounded-md border border-default bg-default text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
          </label>
          <div class="flex justify-end gap-2 pt-1">
            <UButton size="xs" color="neutral" variant="ghost" @click="isOpen = false">
              {{ t('common.cancel') }}
            </UButton>
            <UButton size="xs" color="primary" @click="applyDrafts">
              {{ t('common.apply') }}
            </UButton>
          </div>
        </div>
      </div>
    </template>
  </UPopover>
</template>

<style scoped>
.vellum-preset {
  width: 100%;
  text-align: left;
  font-size: 13px;
  font-family: var(--font-sans);
  padding: 0.375rem 0.5rem;
  border-radius: var(--radius-sm);
  color: var(--ui-text-muted);
  transition:
    background-color var(--t-instant) var(--ease-out),
    color var(--t-instant) var(--ease-out);
}
.vellum-preset:hover {
  color: var(--ui-text);
  background: color-mix(in oklab, var(--color-graphite-500) 8%, transparent);
}
.vellum-preset--active {
  color: var(--color-ember-700);
  background: color-mix(in oklab, var(--color-ember-500) 10%, transparent);
}
:global(html.dark) .vellum-preset--active {
  color: var(--color-ember-300);
}
</style>
