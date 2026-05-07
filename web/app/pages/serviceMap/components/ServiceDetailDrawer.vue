<script setup lang="ts">
/**
 * Side panel that surfaces stats for the selected service. Reuses
 * existing infrastructure: the Top-N aggregation endpoint for "top
 * operations on this service", the standard /traces page for
 * drill-down. The drawer itself is the project's
 * <see cref="AppDrawer"/> so its open/close animation matches the
 * logs-detail and dashboard-config drawers.
 */
import { computed, ref, watch } from 'vue'
import AppDrawer from '~/components/overlay/AppDrawer.vue'
import type {
  ServiceMapDto,
  TimeWindow,
  TraceAggregationItemDto
} from '~/services/types'

const props = defineProps<{
  service: string | null
  data: ServiceMapDto
  range: TimeWindow
}>()

const emit = defineEmits<{
  'close': []
}>()

const { t } = useI18n()
const { $traceService } = useNuxtApp()

const open = computed(() => props.service !== null)

// Single-source normalisation: trim + coerce non-strings to "". The
// graph's force-layout coerces `node.service` to a string when
// constructing positioned nodes, but the API DTOs we look up against
// here are the raw payload — if the wire carried a null or a
// surrounding whitespace the strict `===` would silently miss the
// match and the drawer body would never render. Keeping every match
// site funnelling through this helper avoids that whole class of bug.
function normalizeService(s: string | null | undefined): string {
  return typeof s === 'string' ? s.trim() : ''
}
const targetService = computed(() => normalizeService(props.service))
const isUnnamed = computed(() => props.service !== null && targetService.value.length === 0)

const node = computed(() =>
  props.data.nodes.find(n => normalizeService(n.service) === targetService.value)
)

// Fallback for nodes whose source emitted no `service.name`. Keeps
// the drawer header readable instead of showing a blank line.
const drawerTitle = computed(() => {
  if (props.service === null) return ''
  return targetService.value.length > 0 ? targetService.value : t('serviceMap.unnamedLabel')
})
const incoming = computed(() =>
  props.data.edges.filter(e => normalizeService(e.toService) === targetService.value)
)
const outgoing = computed(() =>
  props.data.edges.filter(e => normalizeService(e.fromService) === targetService.value)
)

const topOps = ref<TraceAggregationItemDto[]>([])
const topLoading = ref(false)
const topError = ref<string | null>(null)
let inFlight = 0

// Dependency nodes are synthesised — no real OTel resource has
// `service.name = "postgresql"`, so the Top-N aggregation (which
// filters on resource service.name) wouldn't find anything for them.
// Same situation for the unnamed node: there's no string identity
// the aggregation can match against. Skip the call in both cases;
// the summary, connections, and drill-down still carry their weight.
watch(() => [targetService.value, node.value?.kind, props.range.from, props.range.to], async () => {
  topOps.value = []
  if (targetService.value.length === 0) return
  if (node.value?.kind !== 'service') return
  const ticket = ++inFlight
  topLoading.value = true
  topError.value = null
  try {
    const response = await $traceService.aggregate({
      from: props.range.from,
      to: props.range.to,
      metric: 'count',
      limit: 5,
      services: [targetService.value]
    })
    if (ticket !== inFlight) return
    topOps.value = response.items
  } catch (e) {
    if (ticket === inFlight) topError.value = e instanceof Error ? e.message : String(e)
  } finally {
    if (ticket === inFlight) topLoading.value = false
  }
}, { immediate: true })

// Render-safe label for an edge endpoint. Falls back to the unnamed
// placeholder for empty strings AND for unexpected non-strings (a null
// here used to crash the entire drawer body when hideUnnamed was off
// and a real service had an incoming edge from the unnamed node —
// `null.trim()` throws and the whole render function aborts).
function edgeLabel(s: string | null | undefined): string {
  if (typeof s !== 'string') return t('serviceMap.unnamedLabel')
  return s.trim().length > 0 ? s : t('serviceMap.unnamedLabel')
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

// Drill-down target for /traces, computed from the selected node so
// the link is a *declarative* prop on the button (`to=...`) instead
// of an imperative click handler. Routing through the `to` prop —
// which UButton resolves to a NuxtLink — makes the navigation a
// native browser anchor click: middle-click opens a tab, right-click
// gets a context menu, and there's no chance of a silently-dropped
// click handler when the surrounding component lifecycle is shifting.
//
// Returns `null` for cases that shouldn't drill down (a dependency
// node whose `attributeKey` is missing) — paired with `v-if` on the
// button it disables the affordance entirely.
const drillDownTarget = computed<{ path: string; query: Record<string, string> } | null>(() => {
  const baseQuery: Record<string, string> = {
    from: props.range.from,
    to: props.range.to
  }
  if (isUnnamed.value) {
    baseQuery.noService = 'true'
    return { path: '/traces', query: baseQuery }
  }
  // Fall back to `service` kind when the lookup hasn't resolved yet —
  // the selected identity is enough to build the link.
  const kind = node.value?.kind ?? 'service'
  if (kind === 'service') {
    if (targetService.value.length > 0) {
      baseQuery.services = targetService.value
      return { path: '/traces', query: baseQuery }
    }
    return null
  }
  if (node.value?.attributeKey && targetService.value.length > 0) {
    baseQuery.attr = `${node.value.attributeKey}:${targetService.value}`
    return { path: '/traces', query: baseQuery }
  }
  return null
})

const canDrillDown = computed(() => drillDownTarget.value !== null)

const viewTracesLabel = computed(() => {
  if (isUnnamed.value) return t('serviceMap.detail.viewTracesUnnamed')
  if (node.value?.kind === 'dependency') return t('serviceMap.detail.viewTracesDependency')
  return t('serviceMap.detail.viewTraces')
})
</script>

<template>
  <AppDrawer
    name="service-map-detail"
    :open="open"
    :title="drawerTitle"
    @update:open="(v: boolean) => { if (!v) emit('close') }"
  >
    <div class="flex flex-col gap-4">
      <!-- Explainer for the (unnamed) node — its summary stats alone
           don't tell the user *what* it is or *why* it has no name.
           Surfaced even before the data lookup so the drawer is
           useful while the underlying data is mid-flight. -->
      <section
        v-if="isUnnamed"
        class="rounded-md border border-default p-3 text-mono-sm"
        :style="{ background: 'color-mix(in oklab, var(--color-graphite-500) 6%, transparent)' }"
      >
        <div class="flex items-center gap-2 mb-1.5 text-default">
          <UIcon name="i-ph-question" class="size-4 text-muted" />
          <span class="text-overline">{{ t('serviceMap.detail.unnamedTitle') }}</span>
        </div>
        <p class="text-muted leading-relaxed">{{ t('serviceMap.detail.unnamedExplainer') }}</p>
      </section>

      <!-- Headline stats. Renders whenever a node was found; for the
           unnamed case the request count / error rate still apply. -->
      <section v-if="node">
        <h3 class="text-overline text-muted">{{ t('serviceMap.detail.summary') }}</h3>
        <dl class="mt-2 grid grid-cols-2 gap-3">
          <div>
            <dt class="text-xs text-muted">{{ t('serviceMap.detail.requests') }}</dt>
            <dd class="text-mono-md">{{ fmtCount(node.requestCount) }}</dd>
          </div>
          <div>
            <dt class="text-xs text-muted">{{ t('serviceMap.detail.errorRate') }}</dt>
            <dd
              class="text-mono-md"
              :class="node.errorCount > 0 ? 'text-error' : ''"
            >{{ fmtRate(node.errorCount, node.requestCount) }}</dd>
          </div>
        </dl>
      </section>

      <!-- Edges. Useful even for the unnamed node — confirms what
           it talks to (or that it's isolated). -->
      <section v-if="incoming.length > 0 || outgoing.length > 0">
        <h3 class="text-overline text-muted">{{ t('serviceMap.detail.connections') }}</h3>
        <div class="mt-2 flex flex-col gap-1">
          <div
            v-for="(e, i) in incoming"
            :key="`in-${i}`"
            class="flex items-center gap-2 text-sm"
          >
            <UIcon name="i-ph-arrow-right" class="size-3.5 text-muted shrink-0" />
            <span class="font-mono text-xs">{{ edgeLabel(e.fromService) }}</span>
            <span class="text-muted text-xs ml-auto">{{ fmtCount(e.callCount) }} {{ t('serviceMap.detail.calls') }}</span>
          </div>
          <div
            v-for="(e, i) in outgoing"
            :key="`out-${i}`"
            class="flex items-center gap-2 text-sm"
          >
            <UIcon name="i-ph-arrow-line-right" class="size-3.5 text-muted shrink-0" />
            <span class="font-mono text-xs">{{ edgeLabel(e.toService) }}</span>
            <span class="text-muted text-xs ml-auto">{{ fmtCount(e.callCount) }} {{ t('serviceMap.detail.calls') }}</span>
          </div>
        </div>
      </section>

      <!-- Top operations on this service. Hidden for dependency
           nodes (they don't emit spans of their own) and for the
           unnamed node (the aggregate filter has no string identity
           to bind to). -->
      <section v-if="node && node.kind === 'service' && !isUnnamed">
        <h3 class="text-overline text-muted">{{ t('serviceMap.detail.topOperations') }}</h3>
        <div v-if="topLoading" class="mt-2 text-mono-sm text-muted">
          {{ t('common.loading') }}
        </div>
        <div v-else-if="topError" class="mt-2 text-mono-sm text-error">
          {{ topError }}
        </div>
        <div v-else-if="topOps.length === 0" class="mt-2 text-mono-sm text-muted">
          {{ t('dashboard.widgets.noData') }}
        </div>
        <ul v-else class="mt-2 flex flex-col gap-1">
          <li v-for="op in topOps" :key="op.key" class="flex items-center gap-2 text-sm">
            <span class="truncate" :title="op.key">{{ op.key }}</span>
            <span class="ml-auto font-mono text-xs text-muted">{{ fmtCount(op.count) }}</span>
            <span
              v-if="op.errorCount > 0"
              class="font-mono text-xs text-error"
            >{{ fmtRate(op.errorCount, op.count) }}</span>
          </li>
        </ul>
      </section>

      <!-- Drill-down link. Hoisted out of the `v-if="node"` so the
           button is available even if the node lookup failed (which
           shouldn't happen with the normalised match, but the button
           remains the user's escape hatch into /traces regardless). -->
      <UButton
        v-if="canDrillDown && drillDownTarget"
        :to="drillDownTarget"
        size="sm"
        color="primary"
        variant="subtle"
        block
        @click="emit('close')"
      >
        <UIcon name="i-ph-tree-structure" class="size-4" />
        {{ viewTracesLabel }}
      </UButton>
    </div>
  </AppDrawer>
</template>
