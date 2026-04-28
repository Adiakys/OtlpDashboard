<script setup lang="ts">
import type { ButtonProps } from '@nuxt/ui'

/**
 * Icon-only button with mandatory aria-label and a tooltip rendered in the
 * same component. Wraps UButton so we never end up with bare icon buttons
 * lacking accessible labels.
 */
defineOptions({ inheritAttrs: false })

withDefaults(defineProps<{
  icon: string
  ariaLabel: string
  tooltip?: string
  tooltipSide?: 'top' | 'right' | 'bottom' | 'left'
  size?: ButtonProps['size']
  color?: ButtonProps['color']
  variant?: ButtonProps['variant']
  loading?: boolean
  disabled?: boolean
}>(), {
  size: 'sm',
  color: 'neutral',
  variant: 'ghost',
  tooltipSide: 'bottom'
})
</script>

<template>
  <UTooltip :text="tooltip ?? ariaLabel" :content="{ side: tooltipSide }">
    <UButton
      v-bind="$attrs"
      :icon="icon"
      :size="size"
      :color="color"
      :variant="variant"
      :loading="loading"
      :disabled="disabled"
      :aria-label="ariaLabel"
      square
      class="transition-colors"
    />
  </UTooltip>
</template>
