<script setup lang="ts">
const props = defineProps<{
  /** Total row count rendered (after client-side filtering, if any). */
  count: number
  /** Total row count of the underlying dataset, when different from count. */
  total?: number
}>()

const { t } = useI18n()

const label = computed(() => {
  if (props.total != null && props.total !== props.count) return `${props.count} / ${props.total}`
  return String(props.count)
})

defineSlots<{
  default?: () => unknown
}>()
</script>

<template>
  <div class="flex items-center justify-between px-3 py-2 text-xs text-muted border-b border-default bg-elevated/40">
    <span>{{ label }} {{ t('common.results') }}</span>
    <slot />
  </div>
</template>
