<script setup lang="ts">
withDefaults(defineProps<{
  open: boolean
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  /** When true, the confirm button is rendered with the destructive color. */
  destructive?: boolean
  busy?: boolean
}>(), {
  destructive: false,
  busy: false
})

const emit = defineEmits<{
  'update:open': [value: boolean]
  confirm: []
  cancel: []
}>()

const { t } = useI18n()

function close() {
  emit('update:open', false)
  emit('cancel')
}
</script>

<template>
  <UModal
    :open="open"
    :title="title"
    @update:open="(v) => v ? emit('update:open', v) : close()"
  >
    <template #body>
      <p class="text-sm text-default leading-relaxed mb-4">{{ message }}</p>
      <div class="flex justify-end gap-2">
        <UButton type="button" color="neutral" variant="ghost" :disabled="busy" @click="close">
          {{ cancelLabel ?? t('common.cancel') }}
        </UButton>
        <UButton
          type="button"
          :color="destructive ? 'error' : 'primary'"
          :loading="busy"
          @click="emit('confirm')"
        >
          {{ confirmLabel ?? t('common.ok') }}
        </UButton>
      </div>
    </template>
  </UModal>
</template>
