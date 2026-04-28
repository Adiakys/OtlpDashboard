<script setup lang="ts">
defineProps<{
  isDirty: boolean
  isSaving: boolean
}>()

defineEmits<{
  'add-widget': []
  save: []
  cancel: []
}>()

const { t } = useI18n()
</script>

<template>
  <div class="flex items-center gap-2">
    <UButton
      icon="i-lucide-plus"
      size="sm"
      color="primary"
      variant="soft"
      @click="$emit('add-widget')"
    >
      {{ t('dashboard.actions.addWidget') }}
    </UButton>

    <UButton
      icon="i-lucide-x"
      size="sm"
      color="neutral"
      variant="ghost"
      @click="$emit('cancel')"
    >
      {{ t('dashboard.actions.cancel') }}
    </UButton>

    <UButton
      icon="i-lucide-save"
      size="sm"
      color="primary"
      :loading="isSaving"
      :disabled="!isDirty || isSaving"
      @click="$emit('save')"
    >
      {{ t('dashboard.actions.save') }}
    </UButton>
  </div>
</template>
