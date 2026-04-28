<script setup lang="ts">
withDefaults(defineProps<{
  icon?: string
  title: string
  description?: string
}>(), {
  icon: 'i-lucide-inbox'
})
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col items-center justify-center text-center gap-3 px-6 py-10">
    <div class="size-12 rounded-full bg-elevated flex items-center justify-center text-muted">
      <UIcon :name="icon" class="size-6" />
    </div>
    <h3 class="text-base font-medium">
      {{ title }}
    </h3>
    <p v-if="description" class="text-sm text-muted max-w-sm">
      {{ description }}
    </p>
    <div v-if="$slots.actions" class="mt-1">
      <slot name="actions" />
    </div>
  </div>
</template>
