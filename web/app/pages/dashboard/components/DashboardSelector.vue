<script setup lang="ts">
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
    class="min-w-48"
    @update:model-value="onUpdate"
  />
</template>
