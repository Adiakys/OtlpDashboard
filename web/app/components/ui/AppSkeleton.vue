<script setup lang="ts">
withDefaults(defineProps<{
  /** Number of skeleton rows to render. */
  rows?: number
  /** Each row height in pixels (Tailwind h-X must accommodate, defaults to h-4). */
  rowClass?: string
  /** When true, render a single block instead of multiple lines. */
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
    class="rounded-md bg-elevated animate-pulse"
    :class="rowClass"
  />
  <div v-else class="space-y-2">
    <div
      v-for="i in rows"
      :key="i"
      class="rounded-md bg-elevated animate-pulse"
      :class="rowClass"
      :style="{ width: `${60 + ((i * 13) % 40)}%` }"
    />
  </div>
</template>
