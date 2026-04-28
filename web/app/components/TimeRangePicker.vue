<script setup lang="ts">
import type { TimeWindow } from '~/services/types'

/**
 * Time-range picker that emits UTC ISO-8601 strings. The backend rejects
 * non-UTC values, so we always normalize through `Date.toISOString()`.
 *
 * Two inputs + preset shortcuts. Keeps the UX simple; good enough for v1.
 */
const model = defineModel<TimeWindow>({ required: true })

defineProps<{
  disabled?: boolean
}>()

interface Preset {
  label: string
  minutes: number
}

const presets: Preset[] = [
  { label: '15m', minutes: 15 },
  { label: '1h', minutes: 60 },
  { label: '6h', minutes: 6 * 60 },
  { label: '24h', minutes: 24 * 60 }
]

// Convert UTC ISO → input value in local timezone (datetime-local inputs work
// in local time). Convert local datetime-local string → UTC ISO on emit.
function isoToLocalInput(iso: string): string {
  const d = new Date(iso)
  // yyyy-MM-ddTHH:mm (trim to minutes for readability)
  const pad = (n: number) => n.toString().padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function localInputToIso(value: string): string {
  // value is in local tz; `new Date(value)` interprets it as local, then
  // toISOString() produces the UTC string.
  return new Date(value).toISOString()
}

const fromLocal = computed({
  get: () => isoToLocalInput(model.value.from),
  set: (v: string) => { model.value = { ...model.value, from: localInputToIso(v) } }
})

const toLocal = computed({
  get: () => isoToLocalInput(model.value.to),
  set: (v: string) => { model.value = { ...model.value, to: localInputToIso(v) } }
})

function applyPreset(minutes: number) {
  const to = new Date()
  const from = new Date(to.getTime() - minutes * 60 * 1000)
  model.value = { from: from.toISOString(), to: to.toISOString() }
}
</script>

<template>
  <div class="flex flex-wrap items-end gap-2">
    <label class="flex flex-col text-xs text-muted">
      From (local)
      <input
        v-model="fromLocal"
        type="datetime-local"
        :disabled="disabled"
        class="mt-1 px-2 py-1 rounded border border-default bg-default text-sm disabled:opacity-50 disabled:cursor-not-allowed"
      >
    </label>

    <label class="flex flex-col text-xs text-muted">
      To (local)
      <input
        v-model="toLocal"
        type="datetime-local"
        :disabled="disabled"
        class="mt-1 px-2 py-1 rounded border border-default bg-default text-sm disabled:opacity-50 disabled:cursor-not-allowed"
      >
    </label>

    <div class="flex gap-1">
      <UButton
        v-for="p in presets"
        :key="p.label"
        size="xs"
        color="neutral"
        variant="outline"
        :disabled="disabled"
        @click="applyPreset(p.minutes)"
      >
        Last {{ p.label }}
      </UButton>
    </div>
  </div>
</template>
