<script setup lang="ts">
defineProps<{
  isLive: boolean
}>()

defineEmits<{ toggle: [] }>()

const { t } = useI18n()
</script>

<template>
  <UButton
    :color="isLive ? 'success' : 'neutral'"
    :variant="isLive ? 'subtle' : 'outline'"
    size="sm"
    class="transition-colors"
    @click="$emit('toggle')"
  >
    <span
      class="inline-block rounded-full size-2 transition-colors"
      :class="isLive ? 'bg-success animate-pulse' : 'bg-muted'"
    />
    {{ isLive ? t('common.live') : t('common.goLive') }}
  </UButton>
</template>
