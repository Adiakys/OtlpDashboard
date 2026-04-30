<script setup lang="ts">
import type { ICellRendererParams } from 'ag-grid-community'
import AppBadge from '~/components/ui/AppBadge.vue'

const props = defineProps<{ params: ICellRendererParams<unknown, string> }>()

const status = computed<string>(() => (props.params.value as string) ?? '·')
</script>

<template>
  <AppBadge :tone="{ kind: 'trace-status', status }" size="xs">
    {{ status }}
  </AppBadge>
</template>
