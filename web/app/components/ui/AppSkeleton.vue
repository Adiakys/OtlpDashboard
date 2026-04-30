<script setup lang="ts">
withDefaults(defineProps<{
  /** Number of skeleton rows to render. */
  rows?: number
  /** Row height class (Tailwind h-X). */
  rowClass?: string
  /** When true, render a single block. */
  block?: boolean
}>(), {
  rows: 4,
  rowClass: 'h-4',
  block: false
})
</script>

<template>
  <div
    v-if="block"
    class="vellum-shimmer relative overflow-hidden"
    :class="rowClass"
    :style="{
      backgroundColor: 'color-mix(in oklab, var(--color-graphite-500) 10%, transparent)',
      borderRadius: 'var(--radius-sm)'
    }"
  />
  <div v-else class="flex flex-col gap-2">
    <div
      v-for="i in rows"
      :key="i"
      class="vellum-shimmer relative overflow-hidden"
      :class="rowClass"
      :style="{
        width: `${60 + ((i * 13) % 40)}%`,
        backgroundColor: 'color-mix(in oklab, var(--color-graphite-500) 10%, transparent)',
        borderRadius: 'var(--radius-sm)'
      }"
    />
  </div>
</template>
