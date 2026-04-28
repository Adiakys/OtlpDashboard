<script setup lang="ts">
defineSlots<{
  toolbar?: (props: object) => unknown
  default?: (props: object) => unknown
}>()
</script>

<template>
  <div class="h-full flex flex-col min-h-0">
    <header
      v-if="$slots.toolbar"
      class="shrink-0 border-b border-default bg-default px-6 py-3"
    >
      <slot name="toolbar" />
    </header>
    <div class="flex-1 min-h-0 min-w-0 flex flex-col overflow-hidden p-6">
      <slot />
    </div>
  </div>
</template>
