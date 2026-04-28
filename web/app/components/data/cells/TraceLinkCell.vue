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
    class="inline-flex items-center gap-1 font-mono text-xs text-primary hover:underline"
    :title="traceId"
    @click.stop="open"
  >
    <UIcon name="i-lucide-waypoints" class="size-3" />
    {{ traceId.slice(0, 8) }}
  </NuxtLink>
  <span v-else class="text-muted">—</span>
</template>
