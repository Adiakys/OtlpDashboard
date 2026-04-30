<script setup lang="ts">
const { t } = useI18n()

withDefaults(defineProps<{
  title?: string
  description?: string
  icon?: string
  retryable?: boolean
}>(), {
  icon: 'i-ph-warning-circle',
  retryable: true
})

defineEmits<{ retry: [] }>()
</script>

<template>
  <div class="flex-1 min-h-0 flex flex-col items-start text-left gap-4 max-w-[55ch] mx-auto px-6 pt-[12vh] pb-10">
    <UIcon :name="icon" class="size-8 shrink-0" style="color: var(--color-rust-500);" />
    <h3 class="text-headline" style="color: var(--color-rust-700);">
      {{ title ?? t('common.error') }}
    </h3>
    <p v-if="description" class="text-body text-muted break-words">
      {{ description }}
    </p>
    <UButton
      v-if="retryable"
      size="sm"
      color="primary"
      variant="solid"
      icon="i-ph-arrow-clockwise"
      @click="$emit('retry')"
    >
      {{ t('common.retry') }}
    </UButton>
  </div>
</template>

<style scoped>
:global(html.dark) h3 {
  color: var(--color-rust-300) !important;
}
</style>
