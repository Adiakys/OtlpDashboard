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
    icon: l.code === locale.value ? 'i-ph-check' : undefined
  }))
})
</script>

<template>
  <UDropdownMenu :items="[items]">
    <UTooltip :text="t('locale.label')" :disabled="!collapsed">
      <button
        type="button"
        class="w-full flex items-center gap-2.5 px-2 py-1.5 text-sm text-muted hover:text-default hover:bg-elevated/60 transition-colors"
        :aria-label="t('locale.label')"
      >
        <UIcon name="i-ph-translate" class="size-4 shrink-0" />
        <span
          v-if="!collapsed"
          class="truncate uppercase"
          style="font-family: var(--font-mono); font-size: 11px; letter-spacing: 0.08em;"
        >{{ current }}</span>
      </button>
    </UTooltip>
  </UDropdownMenu>
</template>
