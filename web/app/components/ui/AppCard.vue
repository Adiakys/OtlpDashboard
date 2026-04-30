<script setup lang="ts">
withDefaults(defineProps<{
  /** Hover state for clickable cards. */
  interactive?: boolean
  /** Remove padding (useful when embedding a grid that paints its own surface). */
  flush?: boolean
}>(), {
  interactive: false,
  flush: false
})
</script>

<template>
  <div
    class="vellum-card flex flex-col min-h-0 transition-[background-color,border-color] duration-200 ease-out"
    :class="[
      interactive ? 'vellum-card--interactive cursor-pointer' : '',
      flush ? '' : 'p-4'
    ]"
  >
    <slot />
  </div>
</template>

<style scoped>
/* Refraction edge: 1px highlight on top to suggest a tactile surface,
   tinted shadow toward the warm neutral. */
.vellum-card {
  background: var(--ui-bg-elevated);
  border: 1px solid color-mix(in oklab, var(--color-graphite-500) 18%, transparent);
  border-radius: var(--radius-lg);
  box-shadow: var(--shadow-1), var(--shadow-inset-edge);
}
.vellum-card--interactive:hover {
  border-color: color-mix(in oklab, var(--color-ember-500) 35%, transparent);
}
</style>
