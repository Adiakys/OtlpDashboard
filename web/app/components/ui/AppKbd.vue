<script setup lang="ts">
defineProps<{ label: string }>()
</script>

<template>
  <kbd
    class="inline-flex items-center justify-center min-w-5 h-5 px-1.5 text-mono-sm"
    style="
      border: 1px solid color-mix(in oklab, var(--color-graphite-500) 28%, transparent);
      border-radius: var(--radius-xs);
      background: var(--ui-bg-elevated);
      color: var(--ui-text-muted);
      font-size: 10.5px;
      box-shadow: var(--shadow-inset-edge);
    "
  >
    {{ label }}
  </kbd>
</template>
