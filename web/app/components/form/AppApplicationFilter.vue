<script setup lang="ts">
/**
 * Multi-select picker over `service.name` values. Three pages of state:
 *  - "all" → no filter applied (modelValue is empty AND noneSelected is false)
 *  - "none" → user explicitly cleared every box (noneSelected is true)
 *  - "subset" → modelValue is a non-empty allow-list
 *
 * The "none" state is distinct from "all" because users asked for the
 * literal checkbox semantics: ticking "All applications" off should
 * actually hide every row, not silently fall back to no-filter. The
 * popover layout otherwise mirrors AppSeveritySelect so the toolbar
 * reads as one family of filters.
 */
const props = defineProps<{
  modelValue: string[]
  options: string[]
  /** True when the user has explicitly deselected every application —
   *  distinct from the implicit "all" state that empty `modelValue`
   *  alone encodes. Pages that want the deselect-all affordance bind
   *  this; pages that don't can omit it and the picker behaves as a
   *  plain "all ↔ subset" toggle. */
  noneSelected?: boolean
  /** Optional any-span / root match mode binding. When omitted the
   *  toggle row in the popover is hidden and the filter stays
   *  root-anchored — pages that don't surface the alternative don't
   *  pay for the extra UI. */
  matchMode?: 'root' | 'any'
  disabled?: boolean
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string[]]
  'update:noneSelected': [value: boolean]
  'update:matchMode': [value: 'root' | 'any']
}>()

const { t } = useI18n()
const isOpen = ref(false)

const allSelected = computed(() => !props.noneSelected && props.modelValue.length === 0)
const noneSelected = computed(() => props.noneSelected === true)

function isChecked(name: string): boolean {
  if (noneSelected.value) return false
  return allSelected.value || props.modelValue.includes(name)
}

function emitSelection(next: string[]) {
  // Collapse the explicit full list back to the implicit "all" form so
  // the URL stays compact, and clear the "none" flag whenever any
  // positive selection is emitted.
  if (props.noneSelected) emit('update:noneSelected', false)
  if (next.length === props.options.length) emit('update:modelValue', [])
  else emit('update:modelValue', next)
}

function toggle(name: string) {
  // Independent checkbox semantics: each click flips just that row,
  // materialising from the implicit "all" state on first deselect and
  // collapsing back when the explicit list re-covers every option.
  // Toggling out of the "none" state starts a fresh single-item list.
  if (noneSelected.value) {
    emitSelection([name])
    return
  }
  const explicit = allSelected.value ? [...props.options] : [...props.modelValue]
  const idx = explicit.indexOf(name)
  if (idx >= 0) explicit.splice(idx, 1)
  else explicit.push(name)
  if (explicit.length === 0) {
    // Last box just got unchecked from an explicit list — treat that as
    // the literal "deselect all" the user asked for, not as no-filter.
    emit('update:modelValue', [])
    emit('update:noneSelected', true)
    return
  }
  emitSelection(explicit)
}

function toggleAll() {
  if (allSelected.value) {
    // All → none: encodes the user's explicit "deselect all" intent.
    emit('update:modelValue', [])
    emit('update:noneSelected', true)
  } else {
    // Anything else (none / subset) → all: collapses to the compact
    // implicit-all form and clears the none flag.
    emit('update:modelValue', [])
    if (props.noneSelected) emit('update:noneSelected', false)
  }
}

const buttonLabel = computed(() => {
  if (noneSelected.value) return t('filter.applicationNone')
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
          @click="toggleAll"
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
