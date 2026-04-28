<script setup lang="ts">
defineProps<{ label: string }>()
</script>

<template>
  <kbd class="inline-flex items-center justify-center min-w-5 h-5 px-1.5 rounded border border-default bg-elevated text-[10px] font-mono text-muted">
    {{ label }}
  </kbd>
</template>
