<script setup lang="ts">
import type { TimeWindow } from '~/services/types'

const range = defineModel<TimeWindow>('range', { required: true })
const limit = defineModel<number>('limit', { required: true })
const service = defineModel<string | null>('service', { required: true })

defineProps<{
  live: boolean
  services: string[]
}>()

defineEmits<{ reload: [], toggleLive: [] }>()

const limitOptions = [25, 50, 100, 500]
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

    <LiveToggle
      class="ml-auto"
      :is-live="live"
      @toggle="$emit('toggleLive')"
    />
  </div>
</template>
