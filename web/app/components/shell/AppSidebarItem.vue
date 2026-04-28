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
      class="flex items-center gap-3 px-2.5 py-2 rounded-md text-sm text-default hover:bg-elevated transition-colors"
      :class="collapsed ? 'justify-center' : ''"
      active-class="bg-elevated font-medium text-primary"
    >
      <UIcon :name="item.icon" class="size-4 shrink-0" />
      <Transition name="fade">
        <span v-if="!collapsed" class="truncate">{{ label }}</span>
      </Transition>
    </NuxtLink>
  </UTooltip>
</template>
