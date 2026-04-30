<script setup lang="ts">
import type { ICellRendererParams } from 'ag-grid-community'
import AppBadge from '~/components/ui/AppBadge.vue'
import { severityBucketFromNumber } from '~/types/filters'
import type { LogRecordDto } from '~/services/types'

const props = defineProps<{ params: ICellRendererParams<LogRecordDto, number> }>()

const bucket = computed(() => severityBucketFromNumber(props.params.data?.severityNumber ?? 0))
const label = computed(() => {
  const r = props.params.data
  return r ? (r.severityText ?? String(r.severityNumber)) : ''
})
</script>

<template>
  <AppBadge :tone="{ kind: 'severity', bucket }" size="md" mono>
    {{ label }}
  </AppBadge>
</template>
