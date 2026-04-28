<script setup lang="ts">
import type { ButtonProps } from '@nuxt/ui'

/**
 * Thin wrapper around UButton that fixes our defaults: small size, subtle
 * variant by default, smooth color transition. Pass any UButton prop through.
 */
defineOptions({ inheritAttrs: false })

withDefaults(defineProps<ButtonProps & { tone?: 'primary' | 'subtle' | 'ghost' | 'danger' }>(), {
  size: 'sm'
})

const variantByTone = {
  primary: 'solid',
  subtle: 'subtle',
  ghost: 'ghost',
  danger: 'soft'
} as const

const colorByTone = {
  primary: 'primary',
  subtle: 'neutral',
  ghost: 'neutral',
  danger: 'error'
} as const
</script>

<template>
  <UButton
    v-bind="$attrs"
    :size="size"
    :color="tone ? colorByTone[tone] : ($attrs.color as ButtonProps['color'])"
    :variant="tone ? variantByTone[tone] : ($attrs.variant as ButtonProps['variant'])"
    class="transition-colors"
  >
    <slot />
  </UButton>
</template>
