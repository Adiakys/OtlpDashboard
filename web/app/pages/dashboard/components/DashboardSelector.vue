<script setup lang="ts">
import { computed } from 'vue'
import type { DashboardDto } from '~/services/types'
import { DEFAULT_DASHBOARD_ID } from '~/services/types'

const props = defineProps<{
  dashboards: DashboardDto[]
  currentId: string
  disabled?: boolean
}>()

const emit = defineEmits<{
  change: [id: string]
}>()

const { t } = useI18n()

interface Item {
  value: string
  label: string
}

const items = computed<Item[]>(() =>
  props.dashboards.map(d => ({
    value: d.id,
    label: d.id === DEFAULT_DASHBOARD_ID ? t('dashboard.selector.defaultLabel', { name: d.name }) : d.name
  }))
)

// Enable search once the list is long enough that scanning visually starts
// to bite. Below the threshold the searchable input is just visual noise.
const searchable = computed(() => props.dashboards.length > 6)

function onUpdate(value: string) {
  if (value === props.currentId) return
  emit('change', value)
}
</script>

<template>
  <USelectMenu
    :model-value="currentId"
    :items="items"
    value-key="value"
    :disabled="disabled"
    :searchable="searchable"
    class="min-w-32 sm:min-w-48 max-w-64"
    @update:model-value="onUpdate"
  />
</template>
