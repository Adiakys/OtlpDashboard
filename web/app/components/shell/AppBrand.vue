<script setup lang="ts">
const { $appName, $appVersion } = useNuxtApp()

defineProps<{
  /** When true, only render the logo (sidebar collapsed). */
  compact?: boolean
  /** When true, render large size (login hero). */
  hero?: boolean
}>()
</script>

<template>
  <div class="flex items-center gap-2.5 min-w-0">
    <span
      class="shrink-0 inline-flex items-center justify-center text-default"
      :class="hero ? 'size-12' : 'size-7'"
      aria-hidden="true"
    >
      <!-- Inline Strata logo: three stacked rules + ember signal dot. -->
      <svg
        viewBox="0 0 32 32"
        fill="none"
        :width="hero ? 48 : 28"
        :height="hero ? 48 : 28"
      >
        <line x1="6" y1="11" x2="26" y2="11" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        <line x1="6" y1="16" x2="20" y2="16" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
        <circle cx="22.6" cy="16" r="1.7" fill="var(--color-ember-500)" />
        <line x1="6" y1="21" x2="22" y2="21" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
      </svg>
    </span>
    <Transition name="fade" mode="out-in">
      <div v-if="!compact" class="min-w-0 flex flex-col">
        <span
          class="font-semibold truncate text-default"
          :class="hero ? 'text-display' : 'text-[0.95rem] tracking-[-0.01em]'"
          :title="$appName"
        >{{ $appName }}</span>
        <span
          v-if="$appVersion && !hero"
          class="truncate"
          :title="$appVersion"
          style="font-family: var(--font-mono); font-size: 10px; line-height: 1.4; color: var(--color-graphite-500);"
        >v{{ $appVersion }}</span>
      </div>
    </Transition>
  </div>
</template>
