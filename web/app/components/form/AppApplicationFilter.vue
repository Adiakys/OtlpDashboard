<script setup lang="ts">
/**
 * Multi-select picker over `service.name` values. Empty array is the
 * canonical "no filter" state — mirrors how the severity picker
 * encodes "all buckets" — so pages don't have to invent a sentinel.
 *
 * The popover layout is intentionally identical to AppSeveritySelect
 * (pill button + checkbox list + leading "All" reset row) so the
 * toolbar reads as a coherent family of filters.
 */
const props = defineProps<{
  modelValue: string[]
  options: string[]
  /** Optional any-span / root match mode binding. When omitted the
   *  toggle row in the popover is hidden and the filter stays
   *  root-anchored — pages that don't surface the alternative don't
   *  pay for the extra UI. */
  matchMode?: 'root' | 'any'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string[]]
  'update:matchMode': [value: 'root' | 'any']
}>()

const { t } = useI18n()
const isOpen = ref(false)

const allSelected = computed(() => props.modelValue.length === 0)

function isChecked(name: string): boolean {
  return allSelected.value || props.modelValue.includes(name)
}

function toggle(name: string) {
  // Implicit "all" → explicit "all-but-this" the moment the user
  // de-selects an option. Re-toggling everything back to the full set
  // collapses to the implicit form so the URL stays compact.
  const explicit = allSelected.value ? [...props.options] : [...props.modelValue]
  const idx = explicit.indexOf(name)
  if (idx >= 0) explicit.splice(idx, 1)
  else explicit.push(name)
  if (explicit.length === props.options.length) emit('update:modelValue', [])
  else emit('update:modelValue', explicit)
}

function selectAll() {
  emit('update:modelValue', [])
}

const buttonLabel = computed(() => {
  if (allSelected.value) return t('filter.applicationAll')
  if (props.modelValue.length === 1) return props.modelValue[0]!
  return t('filter.applicationCount', { count: props.modelValue.length })
})

const supportsMatchMode = computed(() => props.matchMode !== undefined)
const isAnySpan = computed(() => props.matchMode === 'any')

function toggleMatchMode() {
  emit('update:matchMode', isAnySpan.value ? 'root' : 'any')
}
</script>

<template>
  <UPopover v-model:open="isOpen">
    <button
      type="button"
      class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      :disabled="disabled"
    >
      <UIcon name="i-ph-stack" class="size-4 text-muted" />
      <span
        class="truncate max-w-[14rem]"
        style="font-family: var(--font-mono); font-size: 12px; letter-spacing: 0.04em;"
      >{{ buttonLabel }}</span>
      <UIcon name="i-ph-caret-down" class="size-3.5 text-muted" />
    </button>

    <template #content>
      <div class="p-2 w-64 space-y-0.5 max-h-80 overflow-y-auto">
        <button
          type="button"
          class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm hover:bg-elevated transition-colors"
          @click="selectAll"
        >
          <UIcon
            :name="allSelected ? 'i-ph-check-square' : 'i-ph-square'"
            class="size-4"
            :class="allSelected ? 'text-primary' : 'text-muted'"
          />
          <span class="font-medium">{{ t('filter.applicationAll') }}</span>
        </button>
        <div
          v-if="options.length > 0"
          class="my-1 border-t border-default"
        />
        <button
          v-for="opt in options"
          :key="opt"
          type="button"
          class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm hover:bg-elevated transition-colors"
          @click="toggle(opt)"
        >
          <UIcon
            :name="isChecked(opt) ? 'i-ph-check-square' : 'i-ph-square'"
            class="size-4"
            :class="isChecked(opt) ? 'text-primary' : 'text-muted'"
          />
          <span class="truncate font-mono text-xs">{{ opt }}</span>
        </button>
        <div
          v-if="options.length === 0"
          class="px-2 py-1.5 text-xs text-muted"
        >{{ t('filter.applicationEmpty') }}</div>

        <!-- Match-mode toggle: opt-in for pages that wire `matchMode`.
             Default (root) keeps the column-aligned semantics the user
             expects from a checkbox; `any` re-enables the discovery
             behaviour for cross-service traces. -->
        <template v-if="supportsMatchMode">
          <div class="my-1 border-t border-default" />
          <button
            type="button"
            class="w-full flex items-start gap-2 px-2 py-1.5 rounded-md text-left hover:bg-elevated transition-colors"
            :title="t('filter.applicationMatchModeHint')"
            @click="toggleMatchMode"
          >
            <UIcon
              :name="isAnySpan ? 'i-ph-check-square' : 'i-ph-square'"
              class="size-4 mt-0.5 shrink-0"
              :class="isAnySpan ? 'text-primary' : 'text-muted'"
            />
            <span class="flex flex-col">
              <span class="font-medium text-sm">{{ t('filter.applicationMatchModeAny') }}</span>
              <span class="text-xs text-muted">{{ t('filter.applicationMatchModeHint') }}</span>
            </span>
          </button>
        </template>
      </div>
    </template>
  </UPopover>
</template>
