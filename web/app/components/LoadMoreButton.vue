<script setup lang="ts">
defineProps<{
  hasMore: boolean
  loading: boolean
}>()

defineEmits<{ click: [] }>()
</script>

<template>
  <div class="flex justify-center py-2">
    <UButton
      v-if="hasMore"
      color="neutral"
      variant="outline"
      :loading="loading"
      @click="$emit('click')"
    >
      Load more
    </UButton>
    <p v-else class="text-xs text-muted">
      End of results
    </p>
  </div>
</template>
