<script setup lang="ts">
import type { DurationRange } from '~/types/filters'

const props = defineProps<{
  modelValue: DurationRange
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: DurationRange] }>()

const { t } = useI18n()
const isOpen = ref(false)

const minDraft = ref<string>('')
const maxDraft = ref<string>('')

function syncDrafts() {
  minDraft.value = props.modelValue.minMs == null ? '' : String(props.modelValue.minMs)
  maxDraft.value = props.modelValue.maxMs == null ? '' : String(props.modelValue.maxMs)
}

watch(() => [props.modelValue.minMs, props.modelValue.maxMs], syncDrafts, { immediate: true })

function apply() {
  const min = minDraft.value === '' ? null : Number(minDraft.value)
  const max = maxDraft.value === '' ? null : Number(maxDraft.value)
  emit('update:modelValue', {
    minMs: Number.isFinite(min as number) ? (min as number) : null,
    maxMs: Number.isFinite(max as number) ? (max as number) : null
  })
  isOpen.value = false
}

function clear() {
  emit('update:modelValue', { minMs: null, maxMs: null })
  isOpen.value = false
}

const summary = computed(() => {
  const { minMs, maxMs } = props.modelValue
  if (minMs == null && maxMs == null) return t('filter.duration')
  if (minMs != null && maxMs != null) return `${minMs}–${maxMs} ms`
  if (minMs != null) return `≥ ${minMs} ms`
  return `≤ ${maxMs} ms`
})

const isActive = computed(() => props.modelValue.minMs != null || props.modelValue.maxMs != null)
</script>

<template>
  <UPopover v-model:open="isOpen">
    <button
      type="button"
      class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      :class="isActive ? 'border-primary/50 text-primary' : ''"
      :disabled="disabled"
    >
      <UIcon name="i-lucide-timer" class="size-4 text-muted" />
      <span class="truncate">{{ summary }}</span>
      <UIcon name="i-lucide-chevron-down" class="size-3.5 text-muted" />
    </button>

    <template #content>
      <div class="p-3 w-64 space-y-3">
        <label class="block">
          <span class="text-xs text-muted">{{ t('filter.minMs') }}</span>
          <input
            v-model="minDraft"
            type="number"
            min="0"
            class="mt-1 w-full px-2 py-1.5 rounded-md border border-default bg-default text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
          >
        </label>
        <label class="block">
          <span class="text-xs text-muted">{{ t('filter.maxMs') }}</span>
          <input
            v-model="maxDraft"
            type="number"
            min="0"
            class="mt-1 w-full px-2 py-1.5 rounded-md border border-default bg-default text-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
          >
        </label>
        <div class="flex justify-between gap-2 pt-1">
          <UButton size="xs" color="neutral" variant="ghost" @click="clear">
            {{ t('common.clear') }}
          </UButton>
          <div class="flex gap-2">
            <UButton size="xs" color="neutral" variant="ghost" @click="isOpen = false">
              {{ t('common.cancel') }}
            </UButton>
            <UButton size="xs" color="primary" @click="apply">
              {{ t('common.apply') }}
            </UButton>
          </div>
        </div>
      </div>
    </template>
  </UPopover>
</template>
