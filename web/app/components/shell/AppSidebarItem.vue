<script setup lang="ts">
import type { NavItem } from '~/types/navigation'

const { t } = useI18n()

const props = defineProps<{
  item: NavItem
  collapsed?: boolean
}>()

const label = computed(() => t(props.item.labelKey))
</script>

<template>
  <UTooltip :text="label" :content="{ side: 'right' }" :disabled="!collapsed">
    <NuxtLink
      :to="item.to"
      class="group relative flex items-center gap-2.5 px-3 py-2 text-sm text-muted hover:text-default transition-colors"
      :class="collapsed ? 'justify-center px-0' : ''"
      active-class="vellum-nav-active"
    >
      <UIcon :name="item.icon" class="size-4 shrink-0" />
      <Transition name="fade">
        <span v-if="!collapsed" class="truncate text-body">{{ label }}</span>
      </Transition>
    </NuxtLink>
  </UTooltip>
</template>

<style scoped>
/* Active indicator: 2px ember rule to the left of the row.
   Functional indicator (selection cue), not decorative side-stripe. */
:deep(.vellum-nav-active) {
  color: var(--ui-text);
  background: color-mix(in oklab, var(--color-graphite-500) 8%, transparent);
  box-shadow: inset 2px 0 0 0 var(--color-ember-500);
}
:deep(.vellum-nav-active) :where(.iconify) {
  color: var(--color-ember-500);
}
</style>
