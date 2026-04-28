<script setup lang="ts">
const nav = [
  { label: 'Dashboard', icon: 'i-lucide-layout-dashboard', to: '/dashboard' },
  { label: 'Traces', icon: 'i-lucide-waypoints', to: '/traces' },
  { label: 'Logs', icon: 'i-lucide-file-text', to: '/logs' },
  { label: 'Metrics', icon: 'i-lucide-chart-line', to: '/metrics' }
]

const { $authStore, $appName, $appVersion } = useNuxtApp()

async function logout() {
  $authStore.clear()
  await navigateTo('/login', { replace: true })
}
</script>

<template>
  <div class="h-screen flex overflow-hidden">
    <aside class="w-56 shrink-0 border-r border-default px-4 py-6 bg-default flex flex-col">
      <div class="flex items-start gap-2 mb-6 px-2">
        <UIcon name="i-lucide-gauge" class="size-5 text-primary shrink-0 mt-0.5" />
        <div class="min-w-0 flex flex-col">
          <span class="font-semibold text-sm truncate" :title="$appName">{{ $appName }}</span>
          <span
            v-if="$appVersion"
            class="text-[10px] font-mono text-muted truncate"
            :title="$appVersion"
          >
            v{{ $appVersion }}
          </span>
        </div>
      </div>

      <nav class="space-y-1">
        <NuxtLink
          v-for="item in nav"
          :key="item.to"
          :to="item.to"
          class="flex items-center gap-2 px-2 py-1.5 rounded text-sm text-default hover:bg-elevated"
          active-class="bg-elevated font-medium text-primary"
        >
          <UIcon :name="item.icon" class="size-4" />
          {{ item.label }}
        </NuxtLink>
      </nav>

      <button
        type="button"
        class="mt-auto flex items-center gap-2 px-2 py-1.5 rounded text-sm text-muted hover:bg-elevated hover:text-default"
        @click="logout"
      >
        <UIcon name="i-lucide-log-out" class="size-4" />
        Logout
      </button>
    </aside>

    <main class="flex-1 min-w-0 min-h-0 flex flex-col overflow-hidden p-6">
      <slot />
    </main>
  </div>
</template>
