<script setup lang="ts">
/**
 * Attribute filter — a popover-driven editor over an array of
 * `key:value` strings. Each entry maps 1:1 to one `?attr=` query
 * parameter on the server (string-typed match — numeric/boolean
 * attributes are out of scope, see `AttributeFilter` on the API).
 *
 * Behaviour:
 *  - Clicking the chip-button opens a popover with two inputs and an
 *    "Add" button. Submitting (button or Enter) pushes the pair onto
 *    `modelValue` and clears the inputs so several pairs can be added
 *    in a row.
 *  - Active pairs render inline in the popover, each with a tiny "×"
 *    that pops them off. The trigger summary doubles as a count
 *    badge so the user sees, at a glance, how many filters are on.
 *
 * The component is intentionally generic: pages own the ref, so the
 * same descriptor wires logs and traces with no kind-specific code.
 */
const props = defineProps<{
  modelValue: string[]
  disabled?: boolean
}>()

const emit = defineEmits<{ 'update:modelValue': [value: string[]] }>()

const { t } = useI18n()
const isOpen = ref(false)

const draftKey = ref('')
const draftValue = ref('')

interface ParsedFilter {
  raw: string
  key: string
  value: string
}

const parsed = computed<ParsedFilter[]>(() =>
  props.modelValue.map(raw => {
    const colon = raw.indexOf(':')
    return colon < 0
      ? { raw, key: raw, value: '' }
      : { raw, key: raw.slice(0, colon), value: raw.slice(colon + 1) }
  })
)

function addDraft() {
  const k = draftKey.value.trim()
  const v = draftValue.value.trim()
  if (!k || !v) return
  // Filter the wire format directly — the server splits on the first
  // colon, so no need to escape colons inside the value (they'd be
  // part of the value as-is).
  emit('update:modelValue', [...props.modelValue, `${k}:${v}`])
  draftKey.value = ''
  draftValue.value = ''
}

function removeAt(idx: number) {
  const next = [...props.modelValue]
  next.splice(idx, 1)
  emit('update:modelValue', next)
}

function clearAll() {
  emit('update:modelValue', [])
  isOpen.value = false
}

const isActive = computed(() => props.modelValue.length > 0)
</script>

<template>
  <UPopover v-model:open="isOpen">
    <button
      type="button"
      class="inline-flex items-center gap-2 px-3 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
      :class="isActive ? 'border-primary/50 text-primary' : ''"
      :disabled="disabled"
    >
      <UIcon name="i-ph-tag" class="size-4 text-muted" />
      <span class="truncate">{{ t('filter.attributes') }}</span>
      <span
        v-if="isActive"
        class="inline-flex items-center justify-center min-w-[1.25rem] h-5 px-1.5 rounded-full bg-primary/15 text-primary text-xs font-mono"
      >{{ modelValue.length }}</span>
      <UIcon name="i-ph-caret-down" class="size-3.5 text-muted" />
    </button>

    <template #content>
      <div class="p-3 w-80 space-y-3">
        <div v-if="parsed.length > 0" class="flex flex-col gap-1">
          <div
            v-for="(p, i) in parsed"
            :key="`${p.raw}-${i}`"
            class="flex items-center gap-2 px-2 py-1 rounded-md bg-elevated text-xs"
          >
            <span class="font-mono text-muted truncate">{{ p.key }}</span>
            <span class="text-muted">=</span>
            <span class="font-mono truncate flex-1" :title="p.value">{{ p.value }}</span>
            <button
              type="button"
              class="size-5 inline-flex items-center justify-center rounded hover:bg-default text-muted hover:text-default"
              :title="t('common.remove')"
              @click="removeAt(i)"
            >
              <UIcon name="i-ph-x" class="size-3" />
            </button>
          </div>
        </div>

        <form class="space-y-2" @submit.prevent="addDraft">
          <label class="block">
            <span class="text-xs text-muted">{{ t('filter.attrKey') }}</span>
            <input
              v-model="draftKey"
              type="text"
              :placeholder="t('filter.attrKeyPlaceholder')"
              class="mt-1 w-full px-2 py-1.5 rounded-md border border-default bg-default text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
          </label>
          <label class="block">
            <span class="text-xs text-muted">{{ t('filter.attrValue') }}</span>
            <input
              v-model="draftValue"
              type="text"
              :placeholder="t('filter.attrValuePlaceholder')"
              class="mt-1 w-full px-2 py-1.5 rounded-md border border-default bg-default text-sm font-mono focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
          </label>

          <div class="flex justify-between gap-2 pt-1">
            <UButton
              v-if="parsed.length > 0"
              size="xs"
              color="neutral"
              variant="ghost"
              @click="clearAll"
            >
              {{ t('common.clear') }}
            </UButton>
            <span v-else />
            <UButton
              size="xs"
              color="primary"
              type="submit"
              :disabled="!draftKey.trim() || !draftValue.trim()"
            >
              {{ t('common.add') }}
            </UButton>
          </div>
        </form>
      </div>
    </template>
  </UPopover>
</template>
