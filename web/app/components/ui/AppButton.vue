<script setup lang="ts">
import type { ButtonProps } from '@nuxt/ui'

/**
 * Thin wrapper around UButton: defaults to size sm + neutral subtle.
 * Adds a tactile :active feedback (translate-y 1px) per Vellum motion.
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
    class="vellum-tactile transition-colors"
  >
    <slot />
  </UButton>
</template>

<style scoped>
.vellum-tactile {
  transition:
    background-color var(--t-instant) var(--ease-out),
    color var(--t-instant) var(--ease-out),
    transform var(--t-instant) var(--ease-out),
    opacity var(--t-instant) var(--ease-out);
}
.vellum-tactile:active:not(:disabled) {
  transform: translateY(1px);
  opacity: 0.92;
}
</style>
