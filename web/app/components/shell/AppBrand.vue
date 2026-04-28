<script setup lang="ts">
const { $appName, $appVersion } = useNuxtApp()

defineProps<{
  /** When true, only render the logo without the textual block (sidebar collapsed). */
  compact?: boolean
  /** When true, render in a larger size suitable for the login screen. */
  hero?: boolean
}>()
</script>

<template>
  <div class="flex items-center gap-2 min-w-0">
    <div
      class="shrink-0 flex items-center justify-center rounded-lg bg-primary/10 text-primary transition-colors"
      :class="hero ? 'size-12' : 'size-8'"
    >
      <UIcon
        name="i-lucide-activity"
        :class="hero ? 'size-7' : 'size-5'"
      />
    </div>
    <Transition name="fade" mode="out-in">
      <div v-if="!compact" class="min-w-0 flex flex-col leading-tight">
        <span
          class="font-semibold truncate"
          :class="hero ? 'text-2xl tracking-tight' : 'text-sm'"
          :title="$appName"
        >{{ $appName }}</span>
        <span
          v-if="$appVersion && !hero"
          class="text-[10px] font-mono text-muted truncate"
          :title="$appVersion"
        >v{{ $appVersion }}</span>
      </div>
    </Transition>
  </div>
</template>
