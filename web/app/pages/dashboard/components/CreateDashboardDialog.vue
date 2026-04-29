<script setup lang="ts">
defineProps<{
  open: boolean
  isCreating?: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  create: [name: string]
}>()

const { t } = useI18n()
const name = ref('')

function reset() {
  name.value = ''
}

function close() {
  emit('update:open', false)
  reset()
}

function submit() {
  const trimmed = name.value.trim()
  if (!trimmed) return
  emit('create', trimmed)
  reset()
}
</script>

<template>
  <UModal
    :open="open"
    :title="t('dashboard.create.title')"
    @update:open="(v) => v ? emit('update:open', v) : close()"
  >
    <template #body>
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <UFormField :label="t('dashboard.create.nameLabel')" required>
          <UInput
            v-model="name"
            :placeholder="t('dashboard.create.namePlaceholder')"
            autofocus
            :maxlength="200"
          />
        </UFormField>

        <div class="flex justify-end gap-2 pt-2">
          <UButton type="button" color="neutral" variant="ghost" :disabled="isCreating" @click="close">
            {{ t('common.cancel') }}
          </UButton>
          <UButton
            type="submit"
            color="primary"
            :loading="isCreating"
            :disabled="!name.trim() || isCreating"
          >
            {{ t('dashboard.create.submit') }}
          </UButton>
        </div>
      </form>
    </template>
  </UModal>
</template>
