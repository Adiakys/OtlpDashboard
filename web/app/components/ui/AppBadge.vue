<script setup lang="ts">
import type { BadgeProps } from '@nuxt/ui'
import type { SeverityBucket, TraceStatusFilter } from '~/types/filters'

type Tone =
  | 'neutral'
  | 'primary'
  | 'success'
  | 'warning'
  | 'error'
  | 'info'
  | { kind: 'severity'; bucket: SeverityBucket }
  | { kind: 'trace-status'; status: 'Ok' | 'Error' | string | TraceStatusFilter }
  | { kind: 'metric-kind'; instrumentKind: string }

const metricKindColor: Record<string, BadgeProps['color']> = {
  Gauge: 'primary',
  Sum: 'success',
  Histogram: 'info',
  ExponentialHistogram: 'info',
  Summary: 'info',
  Unspecified: 'neutral'
}

defineOptions({ inheritAttrs: false })

const props = withDefaults(defineProps<{
  tone?: Tone
  size?: BadgeProps['size']
  variant?: BadgeProps['variant']
}>(), {
  tone: 'neutral',
  size: 'xs',
  variant: 'subtle'
})

const severityColor: Record<SeverityBucket, BadgeProps['color']> = {
  trace: 'neutral',
  debug: 'info',
  info: 'success',
  warn: 'warning',
  error: 'error',
  fatal: 'error'
}

const color = computed<BadgeProps['color']>(() => {
  const tone = props.tone
  if (typeof tone === 'string') {
    if (tone === 'primary') return 'primary'
    if (tone === 'success') return 'success'
    if (tone === 'warning') return 'warning'
    if (tone === 'error') return 'error'
    if (tone === 'info') return 'info'
    return 'neutral'
  }
  if (tone.kind === 'severity') return severityColor[tone.bucket]
  if (tone.kind === 'trace-status') {
    if (tone.status === 'Ok' || tone.status === 'ok') return 'success'
    if (tone.status === 'Error' || tone.status === 'error') return 'error'
    return 'neutral'
  }
  if (tone.kind === 'metric-kind') return metricKindColor[tone.instrumentKind] ?? 'neutral'
  return 'neutral'
})
</script>

<template>
  <UBadge
    v-bind="$attrs"
    :color="color"
    :size="size"
    :variant="variant"
  >
    <slot />
  </UBadge>
</template>
