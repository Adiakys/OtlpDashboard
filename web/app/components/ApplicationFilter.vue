<script setup lang="ts">
/**
 * Shared application (`service.name`) filter.
 *
 * `includeAll=true` (logs/traces): prepends "All applications" — v-model = null
 * means no filter. `includeAll=false` (metrics): user must pick one — v-model
 * stays null until they do.
 */
const model = defineModel<string | null>({ required: true })

const props = defineProps<{
  options: string[]
  includeAll?: boolean
  disabled?: boolean
  /** Placeholder shown in `includeAll=false` mode when no selection is made. */
  placeholder?: string
}>()

const ALL_VALUE = '__ALL__'

const items = computed(() => {
  const list = props.options.map(o => ({ label: o, value: o }))
  if (props.includeAll) {
    return [{ label: 'Tutte le applicazioni', value: ALL_VALUE }, ...list]
  }
  return list
})

const selected = computed<string>({
  get: () => model.value ?? (props.includeAll ? ALL_VALUE : ''),
  set: (v) => {
    model.value = v === ALL_VALUE || v === '' ? null : v
  }
})
</script>

<template>
  <label class="flex flex-col text-xs text-muted">
    Application
    <USelect
      v-model="selected"
      :items="items"
      :disabled="disabled"
      :placeholder="placeholder ?? 'Select an application'"
      class="mt-1 min-w-48"
    />
  </label>
</template>
