<script setup lang="ts">
/**
 * Renders the service column for a trace summary. The root span's
 * service is shown as the primary label; when the trace also touches
 * other services, a `(+N)` suffix surfaces the multi-service shape
 * without taking over the column. The native `title` carries the full
 * list — sticking to the browser's tooltip avoids a heavyweight
 * floating-UI dance for what's a hover-disclosed metadata.
 */
import type { ICellRendererParams } from 'ag-grid-community'
import type { TraceSummaryDto } from '~/services/types'

const { t } = useI18n()

const props = defineProps<{
  params: ICellRendererParams<TraceSummaryDto, string | null>
}>()

const row = computed(() => props.params.data ?? null)
const primary = computed(() => row.value?.serviceName ?? null)
const others = computed(() => row.value?.otherServiceNames ?? [])

const tooltip = computed(() => {
  if (others.value.length === 0) return primary.value ?? ''
  // One service per line — `title` honours newlines and the list is
  // typically 1-3 entries, so a vertical layout is more scannable
  // than a comma-joined string.
  const lines = primary.value ? [primary.value, ...others.value] : [...others.value]
  return [t('traces.serviceCell.tooltipHeader'), ...lines].join('\n')
})
</script>

<template>
  <span class="vellum-cell-mono inline-flex items-baseline gap-1" :title="tooltip">
    <span class="truncate">{{ primary ?? '·' }}</span>
    <span
      v-if="others.length > 0"
      class="vellum-trace-service-badge"
    >(+{{ others.length }})</span>
  </span>
</template>

<style scoped>
.vellum-trace-service-badge {
  font-size: 10px;
  font-weight: 500;
  letter-spacing: 0.02em;
  color: var(--color-graphite-500);
  background: color-mix(in oklab, var(--color-graphite-500) 12%, transparent);
  padding: 0 4px;
  border-radius: 4px;
  cursor: help;
}
</style>
