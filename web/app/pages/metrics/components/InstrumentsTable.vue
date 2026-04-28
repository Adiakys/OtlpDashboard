<script setup lang="ts">
import type { InstrumentDto } from '~/services/types'

defineProps<{
  items: InstrumentDto[]
  loading: boolean
  selected: InstrumentDto | null
}>()

defineEmits<{ select: [InstrumentDto] }>()

function isSelected(item: InstrumentDto, selected: InstrumentDto | null): boolean {
  if (!selected) return false
  return item.resourceHash === selected.resourceHash
    && item.scopeName === selected.scopeName
    && item.name === selected.name
    && item.kind === selected.kind
}

function kindColor(kind: string): 'neutral' | 'primary' | 'success' {
  if (kind === 'Gauge') return 'primary'
  if (kind === 'Sum') return 'success'
  return 'neutral'
}
</script>

<template>
  <div class="border border-default rounded overflow-y-auto">
    <table class="w-full text-sm">
      <thead class="bg-elevated text-left sticky top-0 z-10">
        <tr>
          <th class="px-3 py-2 font-medium">
            Instrument
          </th>
          <th class="px-3 py-2 font-medium">
            Kind
          </th>
          <th class="px-3 py-2 font-medium">
            Scope
          </th>
          <th class="px-3 py-2 font-medium">
            Unit
          </th>
          <th class="px-3 py-2 font-medium text-right">
            Points
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="loading && items.length === 0">
          <td colspan="5" class="px-3 py-6 text-center text-muted">
            Loading…
          </td>
        </tr>
        <tr v-else-if="items.length === 0">
          <td colspan="5" class="px-3 py-6 text-center text-muted">
            No instruments recorded yet.
          </td>
        </tr>
        <tr
          v-for="row in items"
          :key="`${row.resourceHash}|${row.scopeName}|${row.name}|${row.kind}`"
          class="border-t border-default hover:bg-elevated cursor-pointer"
          :class="isSelected(row, selected) ? 'bg-elevated font-medium' : ''"
          @click="$emit('select', row)"
        >
          <td class="px-3 py-2 font-mono text-xs">
            {{ row.name }}
          </td>
          <td class="px-3 py-2">
            <UBadge :color="kindColor(row.kind)" size="sm" variant="subtle">
              {{ row.kind }}
            </UBadge>
          </td>
          <td class="px-3 py-2 text-xs text-muted truncate max-w-xs">
            {{ row.scopeName || '—' }}
          </td>
          <td class="px-3 py-2 text-xs font-mono">
            {{ row.unit || '—' }}
          </td>
          <td class="px-3 py-2 text-xs text-right">
            {{ row.pointCount }}
          </td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
