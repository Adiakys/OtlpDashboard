<script setup lang="ts">
import { SEVERITY_BUCKETS, type SeverityBucket } from '~/types/filters'

const props = defineProps<{
  modelValue: SeverityBucket[]
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: SeverityBucket[]] }>()

const { t } = useI18n()
const isOpen = ref(false)

function isSelected(b: SeverityBucket): boolean {
  return props.modelValue.length === 0 || props.modelValue.includes(b)
}

function toggle(b: SeverityBucket) {
  // Convention: empty list means "all". As soon as the user toggles one off,
  // we materialize the explicit selection.
  const explicit = props.modelValue.length === 0 ? [...SEVERITY_BUCKETS] : [...props.modelValue]
  const idx = explicit.indexOf(b)
  if (idx >= 0) explicit.splice(idx, 1)
  else explicit.push(b)
  // If everything is selected, normalize back to "all" (empty array).
  if (explicit.length === SEVERITY_BUCKETS.length) emit('update:modelValue', [])
  else emit('update:modelValue', explicit)
}

const buttonLabel = computed(() => {
  if (props.modelValue.length === 0) return t('filter.severityAll')
  return props.modelValue.map(b => b.toUpperCase()).join(', ')
})

const colors: Record<SeverityBucket, string> = {
  trace: 'text-muted',
  debug: 'text-info',
  info: 'text-success',
  warn: 'text-warning',
  error: 'text-error',
  fatal: 'text-error'
}
</script>

<template>
  <UPopover v-model:open="isOpen">
    <button
      type="button"
      class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      :disabled="disabled"
    >
      <UIcon name="i-lucide-bar-chart-3" class="size-4 text-muted" />
      <span class="truncate max-w-[12rem]">{{ buttonLabel }}</span>
      <UIcon name="i-lucide-chevron-down" class="size-3.5 text-muted" />
    </button>

    <template #content>
      <div class="p-2 w-44 space-y-0.5">
        <button
          v-for="b in SEVERITY_BUCKETS"
          :key="b"
          type="button"
          class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm hover:bg-elevated transition-colors"
          @click="toggle(b)"
        >
          <UIcon
            :name="isSelected(b) ? 'i-lucide-check-square' : 'i-lucide-square'"
            class="size-4"
            :class="isSelected(b) ? 'text-primary' : 'text-muted'"
          />
          <span class="font-medium uppercase text-xs" :class="colors[b]">{{ b }}</span>
        </button>
      </div>
    </template>
  </UPopover>
</template>
