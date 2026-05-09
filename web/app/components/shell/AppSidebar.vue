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
  // Tear down the server-side session cookie before bouncing to /login.
  // logout() also clears the local "signed-in" flag in its finally branch
  // so a backend hiccup doesn't leave the SPA in a half-logged-out state.
  await $authStore.logout()
  await navigateTo('/login', { replace: true })
}
</script>

<template>
  <aside
    class="shrink-0 flex flex-col overflow-hidden bg-default transition-[width] duration-200 ease-out"
    :class="collapsed ? 'w-14' : 'w-56'"
    :style="{
      borderRight: '1px solid color-mix(in oklab, var(--color-graphite-500) 22%, transparent)'
    }"
  >
    <div
      class="px-3 py-5 flex items-center"
      :class="collapsed ? 'justify-center px-0' : ''"
    >
      <AppBrand :compact="collapsed" />
    </div>

    <nav class="flex-1 px-2 py-2 overflow-y-auto">
      <ul class="flex flex-col">
        <li v-for="item in items" :key="item.to">
          <AppSidebarItem :item="item" :collapsed="collapsed" />
        </li>
      </ul>
    </nav>

    <div class="px-2 py-3 divide-y divide-default/60">
      <div class="pb-1.5">
        <AppLocaleSwitcher :collapsed="collapsed" />
        <AppThemeToggle :collapsed="collapsed" />
      </div>
      <div class="pt-1.5">
        <UTooltip :text="t('nav.logout')" :disabled="!collapsed">
          <button
            type="button"
            class="w-full flex items-center gap-2.5 px-2 py-1.5 text-sm text-muted hover:text-default hover:bg-elevated/60 transition-colors"
            :aria-label="t('nav.logout')"
            @click="logout"
          >
            <UIcon name="i-ph-sign-out" class="size-4 shrink-0" />
            <span v-if="!collapsed" class="truncate text-body">{{ t('nav.logout') }}</span>
          </button>
        </UTooltip>
        <UTooltip :text="collapsed ? t('nav.expand') : t('nav.collapse')" :disabled="!collapsed">
          <button
            type="button"
            class="w-full flex items-center gap-2.5 px-2 py-1.5 text-sm text-muted hover:text-default hover:bg-elevated/60 transition-colors"
            :aria-label="collapsed ? t('nav.expand') : t('nav.collapse')"
            @click="toggle"
          >
            <UIcon
              :name="collapsed ? 'i-ph-arrow-line-right' : 'i-ph-arrow-line-left'"
              class="size-4 shrink-0"
            />
            <span v-if="!collapsed" class="truncate text-body">{{ t('nav.collapse') }}</span>
          </button>
        </UTooltip>
      </div>
    </div>
  </aside>
</template>
