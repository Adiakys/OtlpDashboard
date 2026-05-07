<script setup lang="ts">
/**
 * Top-N rollup of root spans grouped by name. The server returns four
 * metric columns regardless of which one drove the sort; this widget
 * highlights the chosen `metric` and shows the others as secondary
 * data so the user gets context without a refetch on re-sort.
 *
 * Click a row → drill down to /traces with `range`, `service` and
 * `spanNameContains=<row.key>` pre-applied. The router's URL persistence
 * picks those up at the destination.
 */
import BaseWidget from '../components/BaseWidget.vue'
import type { TopTracesConfig, TopTracesMetric } from '../types'
import { WIDGET_REGISTRY } from '../registry'
import { presetToWindow } from '../useWidgetSeries'
import type { TraceAggregationItemDto } from '~/services/types'

const props = withDefaults(defineProps<{
  config: TopTracesConfig
  isEditing: boolean
  liveTick: number
  preview?: boolean
}>(), { preview: false })

defineEmits<{
  edit: []
  remove: []
}>()

const { t } = useI18n()
const router = useRouter()
const { $traceService } = useNuxtApp()

const headerTitle = computed(() =>
  props.config.title || t(WIDGET_REGISTRY['top-traces'].titleKey)
)

const limit = computed(() => Math.max(1, Math.min(100, props.config.limit ?? 10)))
const metric = computed<TopTracesMetric>(() => props.config.metric ?? 'count')

const rows = ref<TraceAggregationItemDto[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
let inFlight = 0

async function load() {
  const ticket = ++inFlight
  loading.value = true
  error.value = null
  try {
    const window = presetToWindow(props.config.range)
    const response = await $traceService.aggregate({
      from: window.from,
      to: window.to,
      metric: metric.value,
      limit: limit.value,
      services: props.config.service ? [props.config.service] : undefined
    })
    if (ticket !== inFlight) return
    rows.value = response.items
  } catch (e) {
    if (ticket === inFlight) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  } finally {
    if (ticket === inFlight) loading.value = false
  }
}

watch(
  () => [props.config.range, props.config.service, props.config.metric, props.config.limit, props.liveTick],
  load,
  { immediate: true }
)

function fmtMs(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}µs`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}
function fmtRate(num: number, den: number): string {
  if (den <= 0) return '0%'
  const pct = (num / den) * 100
  return pct < 1 ? pct.toFixed(2) + '%' : pct.toFixed(0) + '%'
}
function fmtCount(n: number): string {
  if (n < 1000) return String(n)
  if (n < 10_000) return (n / 1000).toFixed(1) + 'k'
  return Math.round(n / 1000) + 'k'
}

/** Drill-down: build a /traces URL pre-filtered to this row's window
 *  and operation. Span-name search is "contains", which means an exact
 *  hit narrows hard while still tolerating route-template variants. */
function openRow(row: TraceAggregationItemDto) {
  if (props.isEditing) return
  const window = presetToWindow(props.config.range)
  const query: Record<string, string> = {
    from: window.from,
    to: window.to,
    spanNameContains: row.key
  }
  if (props.config.service) query.services = props.config.service
  void router.push({ path: '/traces', query })
}

/** Tag the column the user picked as primary so it pops visually
 *  without re-ordering the table layout (count is always first, etc.).
 *  Returns a class string. */
function primaryClass(target: TopTracesMetric): string {
  return target === metric.value ? 'text-default font-medium' : 'text-muted'
}
</script>

<template>
  <BaseWidget
    :title="headerTitle"
    :icon="WIDGET_REGISTRY['top-traces'].icon"
    :is-editing="isEditing"
    :loading="loading"
    :error="error"
    :preview="preview"
    @edit="$emit('edit')"
    @remove="$emit('remove')"
  >
    <template #preview>
      <div class="vellum-preview-top">
        <div class="vellum-preview-top__row">
          <span class="vellum-preview-top__rank">1</span>
          <span class="vellum-preview-top__name">/api/checkout</span>
          <span class="vellum-preview-top__metric">2.4k</span>
        </div>
        <div class="vellum-preview-top__row">
          <span class="vellum-preview-top__rank">2</span>
          <span class="vellum-preview-top__name">/api/orders</span>
          <span class="vellum-preview-top__metric">1.1k</span>
        </div>
        <div class="vellum-preview-top__row">
          <span class="vellum-preview-top__rank">3</span>
          <span class="vellum-preview-top__name">/api/products</span>
          <span class="vellum-preview-top__metric">812</span>
        </div>
      </div>
    </template>

    <div
      v-if="rows.length === 0"
      class="flex-1 min-h-0 flex items-center justify-center text-mono-sm text-muted px-3 text-center"
    >
      {{ t('dashboard.widgets.noData') }}
    </div>
    <div v-else class="flex-1 min-h-0 overflow-auto">
      <table class="w-full text-xs vellum-top-table">
        <thead class="sticky top-0 bg-elevated">
          <tr class="text-left">
            <th class="px-3 py-1.5 text-overline" style="color: var(--color-graphite-500); width: 1.5rem;">#</th>
            <th class="px-3 py-1.5 text-overline" style="color: var(--color-graphite-500);">{{ t('dashboard.col.span') }}</th>
            <th class="px-3 py-1.5 text-overline text-right" style="color: var(--color-graphite-500);">
              {{ t('dashboard.widgets.topTraces.count') }}
            </th>
            <th class="px-3 py-1.5 text-overline text-right" style="color: var(--color-graphite-500);">
              {{ t('dashboard.widgets.topTraces.errorRate') }}
            </th>
            <th class="px-3 py-1.5 text-overline text-right" style="color: var(--color-graphite-500);">
              {{ t('dashboard.widgets.topTraces.avg') }}
            </th>
            <th class="px-3 py-1.5 text-overline text-right" style="color: var(--color-graphite-500);">
              {{ t('dashboard.widgets.topTraces.max') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(row, i) in rows"
            :key="row.key"
            class="vellum-top-row cursor-pointer"
            @click="openRow(row)"
          >
            <td class="px-3 py-1.5 text-mono-sm text-muted text-right">{{ i + 1 }}</td>
            <td class="px-3 py-1.5 text-body truncate max-w-[260px]" :title="row.key">{{ row.key }}</td>
            <td class="px-3 py-1.5 text-mono-sm text-right whitespace-nowrap" :class="primaryClass('count')" style="font-variant-numeric: tabular-nums;">
              {{ fmtCount(row.count) }}
            </td>
            <td
              class="px-3 py-1.5 text-mono-sm text-right whitespace-nowrap"
              :class="[primaryClass('errorRate'), row.errorCount > 0 ? 'text-error' : '']"
              style="font-variant-numeric: tabular-nums;"
            >
              {{ fmtRate(row.errorCount, row.count) }}
            </td>
            <td class="px-3 py-1.5 text-mono-sm text-right whitespace-nowrap" :class="primaryClass('avgMs')" style="font-variant-numeric: tabular-nums;">
              {{ fmtMs(row.avgMs) }}
            </td>
            <td class="px-3 py-1.5 text-mono-sm text-right whitespace-nowrap" :class="primaryClass('maxMs')" style="font-variant-numeric: tabular-nums;">
              {{ fmtMs(row.maxMs) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </BaseWidget>
</template>

<style scoped>
.vellum-top-table thead tr {
  border-bottom: 1px solid color-mix(in oklab, var(--color-graphite-500) 14%, transparent);
}
.vellum-top-row + .vellum-top-row {
  border-top: 1px solid color-mix(in oklab, var(--color-graphite-500) 8%, transparent);
}
.vellum-top-row:hover {
  background: color-mix(in oklab, var(--color-graphite-500) 6%, transparent);
}

.vellum-preview-top {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  padding: 0.45rem 0.6rem;
  font-family: var(--font-mono);
  font-size: 0.7rem;
}
.vellum-preview-top__row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  min-width: 0;
}
.vellum-preview-top__rank {
  width: 0.9rem;
  text-align: right;
  color: var(--color-graphite-500);
}
.vellum-preview-top__name {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--color-graphite-600);
}
:global(html.dark) .vellum-preview-top__name { color: var(--color-graphite-300); }
.vellum-preview-top__metric {
  flex: none;
  font-variant-numeric: tabular-nums;
  color: var(--color-graphite-500);
}
</style>
