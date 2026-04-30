<script setup lang="ts">
const props = withDefaults(defineProps<{
  modelValue: string
  placeholder?: string
  disabled?: boolean
  /** Debounce in milliseconds. 0 disables debouncing. */
  debounce?: number
}>(), {
  debounce: 250
})

const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const { t } = useI18n()
const local = ref(props.modelValue)
let timer: ReturnType<typeof setTimeout> | null = null

watch(() => props.modelValue, (v) => {
  if (v !== local.value) local.value = v
})

function onInput(value: string) {
  local.value = value
  if (props.debounce <= 0) {
    emit('update:modelValue', value)
    return
  }
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => {
    emit('update:modelValue', local.value)
  }, props.debounce)
}

function clear() {
  local.value = ''
  emit('update:modelValue', '')
}

onBeforeUnmount(() => {
  if (timer) clearTimeout(timer)
})
</script>

<template>
  <UInput
    :model-value="local"
    :placeholder="placeholder ?? t('common.search')"
    :disabled="disabled"
    icon="i-ph-magnifying-glass"
    size="sm"
    class="w-56"
    :ui="{ trailing: 'pr-1' }"
    @update:model-value="onInput"
  >
    <template v-if="local" #trailing>
      <button
        type="button"
        class="size-5 inline-flex items-center justify-center rounded text-muted hover:bg-elevated transition-colors"
        :aria-label="t('common.clear')"
        @click="clear"
      >
        <UIcon name="i-ph-x" class="size-3.5" />
      </button>
    </template>
  </UInput>
</template>
