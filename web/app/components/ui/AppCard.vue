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
    class="bg-default border border-default rounded-lg flex flex-col min-h-0 transition-colors"
    :class="[
      interactive ? 'hover:border-primary/40 cursor-pointer' : '',
      flush ? '' : 'p-4'
    ]"
  >
    <slot />
  </div>
</template>
