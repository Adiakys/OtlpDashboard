<script setup lang="ts">
import type { ICellRendererParams } from 'ag-grid-community'

const props = defineProps<{ params: ICellRendererParams<unknown, string | null> }>()

const traceId = computed<string | null>(() => (props.params.value as string | null) ?? null)

function open(e: MouseEvent) {
  if (!traceId.value) return
  e.stopPropagation()
  navigateTo(`/traces/${traceId.value}`)
}
</script>

<template>
  <NuxtLink
    v-if="traceId"
    :to="`/traces/${traceId}`"
    class="vellum-trace-link inline-flex items-center gap-1.5 vellum-cell-mono"
    :title="traceId"
    @click.stop="open"
  >
    <UIcon name="i-ph-tree-structure" class="size-3" />
    {{ traceId.slice(0, 8) }}
  </NuxtLink>
  <span v-else class="text-muted">·</span>
</template>

<style scoped>
.vellum-trace-link {
  color: var(--color-graphite-400);
  text-decoration: none;
  transition: color var(--t-instant) var(--ease-out);
}
.vellum-trace-link:hover {
  color: var(--color-ember-500);
  text-decoration: underline;
  text-decoration-thickness: 1px;
  text-underline-offset: 3px;
}
</style>
