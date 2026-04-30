<script setup lang="ts">
defineSlots<{
  toolbar?: (props: object) => unknown
  default?: (props: object) => unknown
}>()
</script>

<template>
  <div class="h-full flex flex-col min-h-0 bg-default text-default">
    <header
      v-if="$slots.toolbar"
      class="shrink-0 px-6 py-4 bg-default"
      style="border-bottom: 1px solid color-mix(in oklab, var(--color-graphite-500) 18%, transparent);"
    >
      <slot name="toolbar" />
    </header>
    <div class="flex-1 min-h-0 min-w-0 flex flex-col overflow-hidden p-6">
      <slot />
    </div>
  </div>
</template>
