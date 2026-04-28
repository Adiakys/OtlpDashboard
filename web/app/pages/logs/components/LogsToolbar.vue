<script setup lang="ts">
import type { TimeWindow } from '~/services/types'

const range = defineModel<TimeWindow>('range', { required: true })
const limit = defineModel<number>('limit', { required: true })
const service = defineModel<string | null>('service', { required: true })

defineProps<{
  traceId?: string
  live: boolean
  services: string[]
}>()

defineEmits<{ reload: [], toggleLive: [] }>()

const limitOptions = [25, 50, 100, 500]

function shortTraceId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id
}
</script>

<template>
  <div class="flex flex-wrap items-end gap-4">
    <ApplicationFilter
      v-model="service"
      :options="services"
      :include-all="true"
      :disabled="live"
    />

    <TimeRangePicker v-model="range" :disabled="live" />

    <label class="flex flex-col text-xs text-muted">
      Limit
      <USelect v-model="limit" :items="limitOptions" :disabled="live" class="mt-1 w-24" />
    </label>

    <UButton icon="i-lucide-refresh-cw" :disabled="live" @click="$emit('reload')">
      Reload
    </UButton>

    <!-- Clearing the badge navigates to /logs without query: the composable
         re-bootstraps with default time window and no traceId. -->
    <UButton
      v-if="traceId"
      :to="{ path: '/logs' }"
      color="primary"
      variant="subtle"
      size="sm"
      trailing-icon="i-lucide-x"
    >
      Trace: {{ shortTraceId(traceId) }}
    </UButton>

    <LiveToggle
      class="ml-auto"
      :is-live="live"
      @toggle="$emit('toggleLive')"
    />
  </div>
</template>
