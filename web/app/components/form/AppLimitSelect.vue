<script setup lang="ts">
const props = withDefaults(defineProps<{
  modelValue: number
  options?: number[]
  disabled?: boolean
}>(), {
  options: () => [25, 50, 100, 500]
})

const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

const { t } = useI18n()

const items = computed(() => props.options.map(n => ({ label: String(n), value: String(n) })))

const selected = computed<string>({
  get: () => String(props.modelValue),
  set: (v: string) => emit('update:modelValue', Number(v))
})
</script>

<template>
  <USelect
    v-model="selected"
    :items="items"
    :disabled="disabled"
    :placeholder="t('filter.limit')"
    icon="i-ph-list-numbers"
    size="sm"
    class="w-24"
  />
</template>
