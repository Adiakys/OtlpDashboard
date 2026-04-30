<script setup lang="ts">
import type { TimeWindow } from '~/services/types'

interface Preset {
  key: string
  labelKey: string
  minutes: number
}

const presets: Preset[] = [
  { key: '5m', labelKey: 'filter.range5m', minutes: 5 },
  { key: '15m', labelKey: 'filter.range15m', minutes: 15 },
  { key: '1h', labelKey: 'filter.range1h', minutes: 60 },
  { key: '6h', labelKey: 'filter.range6h', minutes: 60 * 6 },
  { key: '24h', labelKey: 'filter.range24h', minutes: 60 * 24 },
  { key: '7d', labelKey: 'filter.range7d', minutes: 60 * 24 * 7 }
]

const props = defineProps<{
  modelValue: TimeWindow
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: TimeWindow] }>()

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
  const to = new Date()
  const from = new Date(to.getTime() - p.minutes * 60_000)
  emit('update:modelValue', { from: from.toISOString(), to: to.toISOString() })
  isOpen.value = false
}

function applyDrafts() {
  if (!draftFrom.value || !draftTo.value) return
  emit('update:modelValue', {
    from: localInputToIso(draftFrom.value),
    to: localInputToIso(draftTo.value)
  })
  isOpen.value = false
}

const matchedPreset = computed<Preset | null>(() => {
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
      <div class="flex w-[420px] max-w-[92vw]">
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
