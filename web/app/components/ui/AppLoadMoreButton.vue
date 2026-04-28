<script setup lang="ts">
const { t } = useI18n()

defineProps<{
  hasMore: boolean
  loading?: boolean
}>()

defineEmits<{ load: [] }>()
</script>

<template>
  <div class="flex items-center justify-center py-3">
    <UButton
      v-if="hasMore"
      size="sm"
      color="neutral"
      variant="subtle"
      icon="i-lucide-arrow-down"
      :loading="loading"
      @click="$emit('load')"
    >
      {{ t('common.loadMore') }}
    </UButton>
    <span v-else class="text-xs text-muted">{{ t('common.endOfResults') }}</span>
  </div>
</template>
