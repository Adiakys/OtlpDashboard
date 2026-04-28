<script setup lang="ts">
import AppBrand from './AppBrand.vue'
import AppSidebarItem from './AppSidebarItem.vue'
import AppThemeToggle from './AppThemeToggle.vue'
import AppLocaleSwitcher from './AppLocaleSwitcher.vue'

const { t } = useI18n()
const { items } = useNavigation()
const { collapsed, toggle } = useSidebarState()
const { $authStore } = useNuxtApp()

async function logout() {
  $authStore.clear()
  await navigateTo('/login', { replace: true })
}
</script>

<template>
  <aside
    class="shrink-0 border-r border-default bg-default flex flex-col overflow-hidden transition-[width] duration-200 ease-out"
    :class="collapsed ? 'w-16' : 'w-60'"
  >
    <div class="px-3 py-4 flex items-center" :class="collapsed ? 'justify-center' : ''">
      <AppBrand :compact="collapsed" />
    </div>

    <nav class="flex-1 px-2 py-2 overflow-y-auto space-y-0.5">
      <AppSidebarItem
        v-for="item in items"
        :key="item.to"
        :item="item"
        :collapsed="collapsed"
      />
    </nav>

    <div class="border-t border-default px-2 py-3 space-y-0.5">
      <AppLocaleSwitcher :collapsed="collapsed" />
      <AppThemeToggle :collapsed="collapsed" />
      <UTooltip :text="t('nav.logout')" :disabled="!collapsed">
        <button
          type="button"
          class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm text-muted hover:bg-elevated hover:text-default transition-colors"
          :aria-label="t('nav.logout')"
          @click="logout"
        >
          <UIcon name="i-lucide-log-out" class="size-4 shrink-0" />
          <span v-if="!collapsed" class="truncate">{{ t('nav.logout') }}</span>
        </button>
      </UTooltip>
      <UTooltip :text="collapsed ? t('nav.expand') : t('nav.collapse')" :disabled="!collapsed">
        <button
          type="button"
          class="w-full flex items-center gap-2 px-2 py-1.5 rounded-md text-sm text-muted hover:bg-elevated hover:text-default transition-colors"
          :aria-label="collapsed ? t('nav.expand') : t('nav.collapse')"
          @click="toggle"
        >
          <UIcon
            :name="collapsed ? 'i-lucide-panel-left-open' : 'i-lucide-panel-left-close'"
            class="size-4 shrink-0 transition-transform"
          />
          <span v-if="!collapsed" class="truncate">{{ t('nav.collapse') }}</span>
        </button>
      </UTooltip>
    </div>
  </aside>
</template>
