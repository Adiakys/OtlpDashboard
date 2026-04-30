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
    class="vellum-widget flex flex-col h-full min-h-0 overflow-hidden"
    :class="isEditing ? 'vellum-widget--editing' : ''"
  >
    <header
      class="widget-handle flex items-center gap-2 px-3 py-2 select-none flex-none"
      :class="isEditing ? 'cursor-grab active:cursor-grabbing' : ''"
      :style="{
        borderBottom: '1px solid color-mix(in oklab, var(--color-graphite-500) 12%, transparent)'
      }"
    >
      <UIcon v-if="icon" :name="icon" class="size-3.5 shrink-0" style="color: var(--color-graphite-500);" />
      <span class="flex-1 text-overline truncate" style="color: var(--color-graphite-500);">
        {{ title }}
      </span>

      <template v-if="isEditing">
        <UButton
          icon="i-ph-gear-six"
          size="xs"
          color="neutral"
          variant="ghost"
          square
          class="vellum-widget__btn"
          :aria-label="t('dashboard.actions.configure')"
          @click.stop="$emit('edit')"
          @mousedown.stop
        />
        <UButton
          icon="i-ph-trash"
          size="xs"
          color="error"
          variant="ghost"
          square
          class="vellum-widget__btn"
          :aria-label="t('dashboard.actions.remove')"
          @click.stop="$emit('remove')"
          @mousedown.stop
        />
      </template>
    </header>

    <div ref="bodyEl" class="widget-body flex-1 min-h-0 min-w-0 relative overflow-hidden">
      <div v-if="showSkeleton" class="absolute inset-0 flex flex-col gap-2 p-4">
        <div class="vellum-shimmer relative overflow-hidden h-3 w-1/3 rounded"
          style="background: color-mix(in oklab, var(--color-graphite-500) 10%, transparent);" />
        <div class="vellum-shimmer relative overflow-hidden flex-1 min-h-0 rounded"
          style="background: color-mix(in oklab, var(--color-graphite-500) 8%, transparent);" />
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
          class="absolute top-2 right-2 inline-flex items-center gap-1.5 px-2 py-1 text-overline pointer-events-none"
          :style="{
            background: 'color-mix(in oklab, var(--ui-bg-elevated) 90%, transparent)',
            color: 'var(--color-graphite-500)',
            borderRadius: 'var(--radius-pill)'
          }"
        >
          <UIcon name="i-ph-circle-notch" class="size-3 animate-spin" />
          <span>{{ t('common.loading') }}</span>
        </div>
      </Transition>

      <div
        v-if="error"
        class="absolute inset-x-3 bottom-2 px-2 py-1.5 text-mono-sm truncate"
        :style="{
          background: 'color-mix(in oklab, var(--color-rust-500) 10%, transparent)',
          color: 'var(--color-rust-700)',
          borderRadius: 'var(--radius-sm)',
          border: '1px solid color-mix(in oklab, var(--color-rust-500) 18%, transparent)'
        }"
        :title="error"
      >
        {{ error }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.vellum-widget {
  background: var(--ui-bg-elevated);
  border: 1px solid color-mix(in oklab, var(--color-graphite-500) 16%, transparent);
  border-radius: var(--radius-xl);
  box-shadow: var(--shadow-1), var(--shadow-inset-edge);
  transition:
    border-color var(--t-fast) var(--ease-out),
    box-shadow var(--t-fast) var(--ease-out);
}
.vellum-widget--editing {
  border-color: color-mix(in oklab, var(--color-ember-500) 35%, transparent);
  box-shadow: var(--shadow-2), var(--shadow-inset-edge),
              inset 0 0 0 1px color-mix(in oklab, var(--color-ember-500) 18%, transparent);
}

.vellum-widget__btn {
  transition: transform var(--t-instant) var(--ease-out);
}
.vellum-widget__btn:active:not(:disabled) {
  transform: translateY(1px);
}
</style>
