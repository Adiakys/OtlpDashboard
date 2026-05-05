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
const router = useRouter()
const { $traceService } = useNuxtApp()

const open = computed(() => props.service !== null)

const node = computed(() => props.data.nodes.find(n => n.service === props.service))
const incoming = computed(() => props.data.edges.filter(e => e.toService === props.service))
const outgoing = computed(() => props.data.edges.filter(e => e.fromService === props.service))

const topOps = ref<TraceAggregationItemDto[]>([])
const topLoading = ref(false)
const topError = ref<string | null>(null)
let inFlight = 0

// Dependency nodes are synthesised — no real OTel resource has
// `service.name = "postgresql"`, so the Top-N aggregation (which
// filters on resource service.name) wouldn't find anything for them.
// Skip the call for dependency kind; the connections / summary
// sections still surface the useful data.
watch(() => [props.service, node.value?.kind, props.range.from, props.range.to], async () => {
  topOps.value = []
  if (!props.service) return
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
      service: props.service
    })
    if (ticket !== inFlight) return
    topOps.value = response.items
  } catch (e) {
    if (ticket === inFlight) topError.value = e instanceof Error ? e.message : String(e)
  } finally {
    if (ticket === inFlight) topLoading.value = false
  }
}, { immediate: true })

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

function viewTraces() {
  if (!props.service) return
  void router.push({
    path: '/traces',
    query: {
      from: props.range.from,
      to: props.range.to,
      service: props.service
    }
  })
}
</script>

<template>
  <AppDrawer
    name="service-map-detail"
    :open="open"
    :title="service ?? ''"
    @update:open="(v: boolean) => { if (!v) emit('close') }"
  >
    <div v-if="node" class="flex flex-col gap-4">
      <!-- Headline stats -->
      <section>
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

      <!-- Edges -->
      <section v-if="incoming.length > 0 || outgoing.length > 0">
        <h3 class="text-overline text-muted">{{ t('serviceMap.detail.connections') }}</h3>
        <div class="mt-2 flex flex-col gap-1">
          <div
            v-for="e in incoming"
            :key="`in-${e.fromService}`"
            class="flex items-center gap-2 text-sm"
          >
            <UIcon name="i-ph-arrow-right" class="size-3.5 text-muted shrink-0" />
            <span class="font-mono text-xs">{{ e.fromService }}</span>
            <span class="text-muted text-xs ml-auto">{{ fmtCount(e.callCount) }} {{ t('serviceMap.detail.calls') }}</span>
          </div>
          <div
            v-for="e in outgoing"
            :key="`out-${e.toService}`"
            class="flex items-center gap-2 text-sm"
          >
            <UIcon name="i-ph-arrow-line-right" class="size-3.5 text-muted shrink-0" />
            <span class="font-mono text-xs">{{ e.toService }}</span>
            <span class="text-muted text-xs ml-auto">{{ fmtCount(e.callCount) }} {{ t('serviceMap.detail.calls') }}</span>
          </div>
        </div>
      </section>

      <!-- Top operations on this service. Hidden for dependency
           nodes — they don't emit spans of their own, just receive
           Client calls from the host services. -->
      <section v-if="node.kind === 'service'">
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

      <!-- Drill-down link. The /traces page filters on `service.name`,
           which only matches real services — for dependency nodes the
           filter would return nothing, so we hide the button. -->
      <UButton
        v-if="node.kind === 'service'"
        size="sm"
        color="primary"
        variant="subtle"
        block
        @click="viewTraces"
      >
        <UIcon name="i-ph-tree-structure" class="size-4" />
        {{ t('serviceMap.detail.viewTraces') }}
      </UButton>
    </div>
  </AppDrawer>
</template>
