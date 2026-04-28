<script setup lang="ts">
const ALL = '__all__'

const props = withDefaults(defineProps<{
  modelValue: string | null
  options: string[]
  includeAll?: boolean
  disabled?: boolean
  placeholder?: string
}>(), {
  includeAll: true
})

const emit = defineEmits<{ 'update:modelValue': [value: string | null] }>()

const { t } = useI18n()

const items = computed(() => {
  const list: Array<{ label: string; value: string }> = []
  if (props.includeAll) list.push({ label: t('filter.applicationAll'), value: ALL })
  for (const opt of props.options) list.push({ label: opt, value: opt })
  return list
})

const selected = computed<string>({
  get: () => props.modelValue ?? ALL,
  set: (v: string) => emit('update:modelValue', v === ALL ? null : v)
})

const placeholder = computed(() => props.placeholder ?? t('filter.applicationSelect'))
</script>

<template>
  <USelect
    v-model="selected"
    :items="items"
    :disabled="disabled"
    :placeholder="placeholder"
    icon="i-lucide-server"
    size="sm"
    class="min-w-48"
  />
</template>
