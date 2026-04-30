<script setup lang="ts">
const { t } = useI18n()
const colorMode = useColorMode()

const cycle: Array<'system' | 'light' | 'dark'> = ['system', 'light', 'dark']

const icon = computed(() => {
  switch (colorMode.preference) {
    case 'light': return 'i-ph-sun'
    case 'dark': return 'i-ph-moon'
    default: return 'i-ph-monitor'
  }
})

const label = computed(() => {
  switch (colorMode.preference) {
    case 'light': return t('theme.light')
    case 'dark': return t('theme.dark')
    default: return t('theme.system')
  }
})

function next() {
  const i = cycle.indexOf(colorMode.preference as typeof cycle[number])
  colorMode.preference = cycle[(i + 1) % cycle.length]!
}

defineProps<{ collapsed?: boolean }>()
</script>

<template>
  <UTooltip :text="`${t('theme.label')}: ${label}`" :disabled="!collapsed">
    <button
      type="button"
      class="w-full flex items-center gap-2.5 px-2 py-1.5 text-sm text-muted hover:text-default hover:bg-elevated/60 transition-colors"
      :aria-label="t('theme.label')"
      @click="next"
    >
      <UIcon :name="icon" class="size-4 shrink-0" />
      <span v-if="!collapsed" class="truncate text-body">{{ label }}</span>
    </button>
  </UTooltip>
</template>
