<script setup lang="ts">
defineProps<{
  title: string
  icon?: string
  isEditing: boolean
  loading?: boolean
  error?: string | null
}>()

defineEmits<{
  edit: []
  remove: []
}>()

const { t } = useI18n()

const bodyEl = ref<HTMLElement | null>(null)
const width = ref(0)
const height = ref(0)

let observer: ResizeObserver | null = null

onMounted(() => {
  if (!bodyEl.value) return
  observer = new ResizeObserver(entries => {
    const entry = entries[0]
    if (!entry) return
    const cr = entry.contentRect
    width.value = cr.width
    height.value = cr.height
  })
  observer.observe(bodyEl.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  observer = null
})
</script>

<template>
  <div class="flex flex-col h-full min-h-0 border border-default rounded-lg bg-default overflow-hidden">
    <header class="widget-handle flex items-center gap-2 px-3 py-2 border-b border-default bg-elevated/40 select-none flex-none">
      <UIcon v-if="icon" :name="icon" class="size-4 shrink-0 text-muted" />
      <span class="flex-1 text-xs font-medium uppercase tracking-wide text-muted truncate">
        {{ title }}
      </span>

      <template v-if="isEditing">
        <UButton
          icon="i-lucide-settings-2"
          size="xs"
          color="neutral"
          variant="ghost"
          :aria-label="t('dashboard.actions.configure')"
          @click.stop="$emit('edit')"
        />
        <UButton
          icon="i-lucide-trash-2"
          size="xs"
          color="error"
          variant="ghost"
          :aria-label="t('dashboard.actions.remove')"
          @click.stop="$emit('remove')"
        />
      </template>
    </header>

    <div ref="bodyEl" class="widget-body flex-1 min-h-0 min-w-0 relative overflow-hidden">
      <div class="absolute inset-0 flex flex-col min-h-0 min-w-0">
        <slot :width="width" :height="height" />
      </div>

      <Transition
        enter-active-class="transition-opacity duration-150"
        leave-active-class="transition-opacity duration-150"
        enter-from-class="opacity-0"
        leave-to-class="opacity-0"
      >
        <div
          v-if="loading"
          class="absolute top-2 right-2 inline-flex items-center gap-1.5 px-2 py-1 rounded bg-elevated/80 text-xs text-muted pointer-events-none"
        >
          <UIcon name="i-lucide-loader-2" class="size-3.5 animate-spin" />
          <span>{{ t('common.loading') }}</span>
        </div>
      </Transition>

      <div
        v-if="error"
        class="absolute inset-x-2 bottom-2 px-2 py-1 rounded bg-error/10 text-xs text-error truncate"
        :title="error"
      >
        {{ error }}
      </div>
    </div>
  </div>
</template>
