<script setup lang="ts">
import { computed } from 'vue'
import type { ResolutionWarning } from '../useWidgetSeries'

/**
 * Header chip surfaced when one or more bindings inside a widget
 * resolve to "ambiguous" — i.e. the logical key matches more than
 * one live instrument and the widget needs an explicit
 * `service.instance.id` pin in its configuration to disambiguate.
 *
 * The chip is non-blocking: the widget keeps rendering its empty
 * state for ambiguous bindings (no arbitrary instance picked), and
 * the chip points the user at the config form for resolution.
 */
const props = defineProps<{
  warnings: ResolutionWarning[]
}>()

const { t } = useI18n()

const visible = computed(() => props.warnings.length > 0)

const tooltipMessage = computed(() => {
  return props.warnings.map(w => {
    if (w.requestedId) {
      // The configured pin doesn't match any live instance.
      const available = w.available.length > 0 ? w.available.join(', ') : '—'
      return t('dashboard.widgets.instanceNotFound', { id: w.requestedId, available })
    }
    return t('dashboard.widgets.ambiguousServiceTitle', { service: w.serviceName ?? '·' })
      + ' — '
      + t('dashboard.widgets.ambiguousService')
  }).join('\n')
})
</script>

<template>
  <span
    v-if="visible"
    class="vellum-warning-chip"
    :title="tooltipMessage"
    :aria-label="t('dashboard.widgets.warningChipLabel')"
    role="status"
  >
    <UIcon name="i-ph-warning" class="size-3" />
    <span v-if="warnings.length > 1" class="vellum-warning-chip__count">{{ warnings.length }}</span>
  </span>
</template>

<style scoped>
.vellum-warning-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.2rem;
  padding: 0.1rem 0.35rem;
  font-family: var(--font-mono);
  font-size: 0.65rem;
  line-height: 1;
  letter-spacing: 0.04em;
  background: color-mix(in oklab, var(--color-amber-500) 14%, transparent);
  color: var(--color-amber-700);
  border: 1px solid color-mix(in oklab, var(--color-amber-500) 30%, transparent);
  border-radius: var(--radius-pill);
  cursor: help;
}
:global(html.dark) .vellum-warning-chip {
  color: var(--color-amber-300);
  background: color-mix(in oklab, var(--color-amber-500) 18%, transparent);
}
.vellum-warning-chip__count {
  font-variant-numeric: tabular-nums;
  font-weight: 600;
}
</style>
