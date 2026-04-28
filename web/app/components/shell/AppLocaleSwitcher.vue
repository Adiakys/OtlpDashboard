<script setup lang="ts">
const { t, locale, locales, setLocale } = useI18n()

defineProps<{ collapsed?: boolean }>()

const current = computed(() => {
  const list = unref(locales) as Array<{ code: string; name?: string }>
  return list.find(l => l.code === locale.value)?.name ?? locale.value.toUpperCase()
})

const items = computed(() => {
  const list = unref(locales) as Array<{ code: string; name?: string }>
  return list.map(l => ({
    label: l.name ?? l.code.toUpperCase(),
    onSelect: () => setLocale(l.code as 'it' | 'en'),
    icon: l.code === locale.value ? 'i-lucide-check' : undefined
  }))
})
</script>

<template>
  <UDropdownMenu :items="[items]">
    <UTooltip :text="t('locale.label')" :disabled="!collapsed">
      <button
        type="button"
        class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm text-muted hover:bg-elevated hover:text-default transition-colors"
        :aria-label="t('locale.label')"
      >
        <UIcon name="i-lucide-languages" class="size-4 shrink-0" />
        <span v-if="!collapsed" class="truncate uppercase">{{ current }}</span>
      </button>
    </UTooltip>
  </UDropdownMenu>
</template>
