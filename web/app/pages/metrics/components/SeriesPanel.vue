<script setup lang="ts">
import type { MetricSeriesDto } from '~/services/types'

defineProps<{
  series: MetricSeriesDto | null
  loading: boolean
}>()

function formatTime(iso: string): string {
  return new Date(iso).toLocaleString()
}

function formatValue(value: number): string {
  if (Number.isInteger(value)) return value.toString()
  return value.toFixed(4).replace(/\.?0+$/, '')
}
</script>

<template>
  <div class="border border-default rounded flex flex-col min-h-0">
    <div v-if="!series && !loading" class="p-6 text-sm text-muted text-center">
      Select an instrument on the left to see its data points.
    </div>

    <div v-else-if="loading && !series" class="p-6 text-sm text-muted text-center">
      Loading…
    </div>

    <template v-else-if="series">
      <div class="px-4 py-3 border-b border-default bg-elevated">
        <div class="flex items-baseline justify-between gap-3">
          <h2 class="font-mono text-sm font-semibold truncate" :title="series.instrument.name">
            {{ series.instrument.name }}
          </h2>
          <UBadge size="sm" variant="subtle">
            {{ series.instrument.kind }}
          </UBadge>
        </div>
        <p v-if="series.instrument.description" class="text-xs text-muted mt-1">
          {{ series.instrument.description }}
        </p>
        <dl class="grid grid-cols-2 gap-x-4 gap-y-1 mt-2 text-xs">
          <dt class="text-muted">
            Unit
          </dt>
          <dd class="font-mono">
            {{ series.instrument.unit || '—' }}
          </dd>
          <dt class="text-muted">
            Temporality
          </dt>
          <dd>
            {{ series.instrument.temporality }}
          </dd>
          <dt class="text-muted">
            Monotonic
          </dt>
          <dd>
            {{ series.instrument.isMonotonic ? 'yes' : 'no' }}
          </dd>
          <dt class="text-muted">
            Scope
          </dt>
          <dd class="font-mono truncate" :title="series.instrument.scopeName">
            {{ series.instrument.scopeName || '—' }}
          </dd>
        </dl>
      </div>

      <div class="overflow-y-auto flex-1 min-h-0">
        <table class="w-full text-sm">
          <thead class="bg-elevated text-left sticky top-0 z-10">
            <tr>
              <th class="px-3 py-2 font-medium">
                Time
              </th>
              <th class="px-3 py-2 font-medium text-right">
                Value
              </th>
              <th class="px-3 py-2 font-medium">
                Attributes
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="series.points.length === 0">
              <td colspan="3" class="px-3 py-6 text-center text-muted">
                No data points.
              </td>
            </tr>
            <tr
              v-for="(p, idx) in series.points"
              :key="idx"
              class="border-t border-default"
            >
              <td class="px-3 py-2 text-xs font-mono whitespace-nowrap">
                {{ formatTime(p.time) }}
              </td>
              <td class="px-3 py-2 text-xs font-mono text-right">
                {{ formatValue(p.value) }}
              </td>
              <td class="px-3 py-2 text-xs font-mono text-muted truncate max-w-md">
                {{ Object.keys(p.attributes).length === 0 ? '—' : JSON.stringify(p.attributes) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>
  </div>
</template>
