<script setup lang="ts">
defineProps<{
  isDirty: boolean
  isSaving: boolean
  /** Disables the delete button — set true for the protected default dashboard. */
  canDeleteDashboard: boolean
}>()

const emit = defineEmits<{
  'add-widget': []
  'add-dashboard': []
  'delete-dashboard': []
  save: []
  cancel: []
  'export-layout': []
  'import-file': [file: File]
}>()

const { t } = useI18n()

// Hidden file input drives the "Import" button. Using a button + ref pattern
// keeps the visible UI consistent with the other toolbar actions; clicking it
// programmatically opens the native picker.
const fileInput = ref<HTMLInputElement | null>(null)

function pickFile() {
  fileInput.value?.click()
}

function onFileChosen(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (file) emit('import-file', file)
  // Reset so picking the same file twice in a row still fires `change`.
  input.value = ''
}
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
      icon="i-lucide-layout-dashboard"
      size="sm"
      color="neutral"
      variant="ghost"
      :title="t('dashboard.actions.addDashboard')"
      :aria-label="t('dashboard.actions.addDashboard')"
      @click="$emit('add-dashboard')"
    />

    <UButton
      icon="i-lucide-trash-2"
      size="sm"
      color="error"
      variant="ghost"
      :title="t('dashboard.actions.deleteDashboard')"
      :aria-label="t('dashboard.actions.deleteDashboard')"
      :disabled="!canDeleteDashboard"
      @click="$emit('delete-dashboard')"
    />

    <UButton
      icon="i-lucide-upload"
      size="sm"
      color="neutral"
      variant="ghost"
      :title="t('dashboard.actions.importLayout')"
      :aria-label="t('dashboard.actions.importLayout')"
      @click="pickFile"
    />

    <UButton
      icon="i-lucide-download"
      size="sm"
      color="neutral"
      variant="ghost"
      :title="t('dashboard.actions.exportLayout')"
      :aria-label="t('dashboard.actions.exportLayout')"
      @click="$emit('export-layout')"
    />

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

    <input
      ref="fileInput"
      type="file"
      accept="application/json,.json"
      class="hidden"
      @change="onFileChosen"
    >
  </div>
</template>
