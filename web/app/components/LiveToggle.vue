<script setup lang="ts">
defineProps<{
  isLive: boolean
}>()

defineEmits<{ toggle: [] }>()
</script>

<template>
  <UButton
    :color="isLive ? 'success' : 'neutral'"
    :variant="isLive ? 'subtle' : 'outline'"
    size="sm"
    @click="$emit('toggle')"
  >
    <span
      class="inline-block rounded-full size-2"
      :class="isLive ? 'bg-success animate-pulse' : 'bg-muted'"
    />
    {{ isLive ? 'Live' : 'Go live' }}
  </UButton>
</template>
