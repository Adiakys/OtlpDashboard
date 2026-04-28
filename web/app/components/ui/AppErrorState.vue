<script setup lang="ts">
const { t } = useI18n()

withDefaults(defineProps<{
  title?: string
  description?: string
  icon?: string
  retryable?: boolean
}>(), {
  icon: 'i-lucide-alert-triangle',
  retryable: true
})

defineEmits<{ retry: [] }>()
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col items-center justify-center text-center gap-3 px-6 py-10">
    <div class="size-12 rounded-full bg-error/10 text-error flex items-center justify-center">
      <UIcon :name="icon" class="size-6" />
    </div>
    <h3 class="text-base font-medium">
      {{ title ?? t('common.error') }}
    </h3>
    <p v-if="description" class="text-sm text-muted max-w-md break-words">
      {{ description }}
    </p>
    <UButton
      v-if="retryable"
      size="sm"
      color="neutral"
      variant="subtle"
      icon="i-lucide-rotate-cw"
      @click="$emit('retry')"
    >
      {{ t('common.retry') }}
    </UButton>
  </div>
</template>
