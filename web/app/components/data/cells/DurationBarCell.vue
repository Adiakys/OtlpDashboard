<script setup lang="ts">
import type { ICellRendererParams } from 'ag-grid-community'

interface DurationBarParams {
  /** Either a static maximum or a getter; the cell will normalize the bar against it. */
  max: number | (() => number)
  /** Optional override of the row property to read; defaults to params.value. */
  value?: (data: unknown) => number
}

type Params = ICellRendererParams<unknown, number> & DurationBarParams

const props = defineProps<{ params: Params }>()

const ms = computed(() => {
  const p = props.params
  if (p.value != null && typeof p.value === 'function') return (p.value as (d: unknown) => number)(p.data)
  return (p.value as number) ?? 0
})

const max = computed(() => {
  const m = props.params.max
  return typeof m === 'function' ? m() : m
})

const ratio = computed(() => {
  const m = Math.max(1, max.value)
  return Math.max(0.02, Math.min(1, ms.value / m))
})

function format(value: number): string {
  if (value < 1) return `${(value * 1000).toFixed(0)}μs`
  if (value < 1000) return `${value.toFixed(1)}ms`
  return `${(value / 1000).toFixed(2)}s`
}
</script>

<template>
  <div class="flex items-center gap-2 w-full">
    <div class="h-1.5 rounded-full bg-elevated overflow-hidden flex-1 max-w-32">
      <div
        class="h-full bg-primary transition-[width] duration-300"
        :style="{ width: `${ratio * 100}%` }"
      />
    </div>
    <span class="font-mono text-xs text-muted shrink-0">{{ format(ms) }}</span>
  </div>
</template>
