<script setup lang="ts">
import { ref } from 'vue'
import { useElementSize } from '~/composables/useElementSize'

withDefaults(defineProps<{
  title: string
  icon?: string
  isEditing: boolean
  loading?: boolean
  error?: string | null
  /**
   * When true the widget renders a skeleton instead of its body. Set on
   * first load (no points + no cached data) so the user sees structure
   * immediately rather than an empty card.
   */
  showSkeleton?: boolean
}>(), {
  loading: false,
  error: null,
  showSkeleton: false
})

defineEmits<{
  edit: []
  remove: []
}>()

const { t } = useI18n()

const bodyEl = ref<HTMLElement | null>(null)
const { width, height } = useElementSize(() => bodyEl.value)
</script>

<template>
  <div
    class="flex flex-col h-full min-h-0 border rounded-lg bg-default overflow-hidden transition-[box-shadow,border-color]"
    :class="isEditing
      ? 'border-primary/40 ring-1 ring-primary/20'
      : 'border-default'"
  >
    <header
      class="widget-handle flex items-center gap-2 px-3 py-1.5 border-b bg-elevated/40 select-none flex-none"
      :class="[
        isEditing ? 'cursor-grab active:cursor-grabbing border-primary/30' : 'border-default'
      ]"
    >
      <UIcon v-if="icon" :name="icon" class="size-4 shrink-0 text-muted" />
      <span class="flex-1 text-[11px] font-medium uppercase tracking-wide text-muted truncate">
        {{ title }}
      </span>

      <template v-if="isEditing">
        <UButton
          icon="i-lucide-settings-2"
          size="xs"
          color="neutral"
          variant="soft"
          :aria-label="t('dashboard.actions.configure')"
          @click.stop="$emit('edit')"
          @mousedown.stop
        />
        <UButton
          icon="i-lucide-trash-2"
          size="xs"
          color="error"
          variant="soft"
          :aria-label="t('dashboard.actions.remove')"
          @click.stop="$emit('remove')"
          @mousedown.stop
        />
      </template>
    </header>

    <div ref="bodyEl" class="widget-body flex-1 min-h-0 min-w-0 relative overflow-hidden">
      <div v-if="showSkeleton" class="absolute inset-0 flex flex-col gap-2 p-3">
        <div class="h-3 w-1/3 rounded bg-elevated animate-pulse" />
        <div class="flex-1 min-h-0 rounded bg-elevated/60 animate-pulse" />
      </div>

      <div v-else class="absolute inset-0 flex flex-col min-h-0 min-w-0">
        <slot :width="width" :height="height" />
      </div>

      <Transition
        enter-active-class="transition-opacity duration-150"
        leave-active-class="transition-opacity duration-150"
        enter-from-class="opacity-0"
        leave-to-class="opacity-0"
      >
        <div
          v-if="loading && !showSkeleton"
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
