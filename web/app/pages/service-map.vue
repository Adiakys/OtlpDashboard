<script setup lang="ts">
import AppPage from '~/components/shell/AppPage.vue'
import AppToolbar from '~/components/shell/AppToolbar.vue'
import AppEmptyState from '~/components/ui/AppEmptyState.vue'
import AppErrorState from '~/components/ui/AppErrorState.vue'
import ServiceMapGraph from './serviceMap/components/ServiceMapGraph.vue'
import ServiceDetailDrawer from './serviceMap/components/ServiceDetailDrawer.vue'
import { useServiceMapPage } from './serviceMap/composables/useServiceMapPage'
import { useIconResolver } from './serviceMap/composables/useIconResolver'
import type { ResolvedServiceMapDto } from './serviceMap/composables/useForceLayout'
import type { ActionDescriptor, FilterDescriptor } from '~/types/toolbar'
import type { PackDto, ServiceMapDto, TimeWindow } from '~/services/types'

const { t, locale } = useI18n()
const {
  $serviceMapService,
  $widgetService,
  $traceRetentionDays,
  $queryMaxWindowHours
} = useNuxtApp()

const page = useServiceMapPage($serviceMapService)

// Pack-supplied service-map icons. Fetched once on mount; the resolver
// reactively rebuilds when a pack install/update reloads the list.
// Failures are non-fatal (the map still renders, just without icons).
const packs = ref<readonly PackDto[]>([])
$widgetService.listPacks()
  .then(list => { packs.value = list })
  .catch(() => { packs.value = [] })

// Pass app.baseURL so the resolver can fold the subpath prefix into the
// pack-supplied imageUrls (otherwise a deploy at /OtlpDashboard/ would
// 404 against /icons/... at the domain root).
const { resolve: resolveIconUrl } = useIconResolver(
  packs,
  useRuntimeConfig().app.baseURL
)

// Hide-unnamed-services preference. Persisted in localStorage so the
// view choice survives reloads — same pattern as the histogram toggle
// in /logs. Default off (showing every node) keeps the backend's
// payload visible without surprises; users opt in to filtering.
const HIDE_UNNAMED_KEY = 'serviceMap.hideUnnamed'
const hideUnnamed = ref(
  import.meta.client && window.localStorage.getItem(HIDE_UNNAMED_KEY) === '1'
)
watch(hideUnnamed, (v) => {
  if (import.meta.client) {
    window.localStorage.setItem(HIDE_UNNAMED_KEY, v ? '1' : '0')
  }
  // If the user hides nameless nodes while one is selected, close
  // the drawer — leaving it open on a node that's no longer on the
  // graph is confusing.
  if (v && page.selected.value !== null && isUnnamed(page.selected.value)) {
    page.selected.value = null
  }
})

// Guard against null/undefined even though the DTO types say `string`:
// this function is also called against `page.selected.value`, and a
// stray null in the network payload (or in a transient state) would
// otherwise crash the page's render with `Cannot access trim on null`.
function isUnnamed(service: string | null | undefined): boolean {
  return !service || service.trim().length === 0
}

// Derived view: applies the hide-unnamed preference to the API payload
// before handing it to the graph and the drawer. Filtering edges that
// touch a hidden node is required — leaving them in produces dangling
// arrows pointing into empty space.
const displayedData = computed<ServiceMapDto>(() => {
  if (!hideUnnamed.value) return page.data.value
  const nodes = page.data.value.nodes.filter(n => !isUnnamed(n.service))
  const kept = new Set(nodes.map(n => n.service))
  const edges = page.data.value.edges.filter(e =>
    kept.has(e.fromService) && kept.has(e.toService)
  )
  return { nodes, edges }
})

// Icon-enriched view passed to the graph. The reactive dependency on
// `packs` (via the resolver closure) is what makes async pack fetches
// reflect into the rendered SVG: once /v1/packs resolves, this
// computed recomputes, the graph receives a fresh data object, and
// its existing watcher rebuilds the layout with proper iconUrls.
const resolvedData = computed<ResolvedServiceMapDto>(() => ({
  nodes: displayedData.value.nodes.map(n => ({
    ...n,
    iconUrl: resolveIconUrl(n.service)
  })),
  edges: displayedData.value.edges
}))

const subtitle = computed(() => {
  const win = describeWindow(page.range.value)
  return t('serviceMap.subtitle', {
    nodeCount: displayedData.value.nodes.length,
    edgeCount: displayedData.value.edges.length,
    window: win
  })
})

function describeWindow(range: TimeWindow): string {
  const f = new Date(range.from)
  const tt = new Date(range.to)
  const fmt = new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' })
  return `${fmt.format(f)} → ${fmt.format(tt)}`
}

// The map is exploratory, not a live monitor — refresh is manual.
// (Live polling would re-tick the simulation every 5s and reshuffle
// node positions just as the user is reading the graph.)
const filters: FilterDescriptor[] = [
  {
    kind: 'time-range',
    modelValue: page.range,
    retentionDays: $traceRetentionDays,
    maxWindowHours: $queryMaxWindowHours
  }
]
const actions: ActionDescriptor[] = [
  { kind: 'refresh', loading: page.isLoading, onClick: () => void page.reload() }
]

const isEmpty = computed(() => !page.isLoading.value && displayedData.value.nodes.length === 0)
</script>

<template>
  <AppPage>
    <template #toolbar>
      <AppToolbar
        :title="t('serviceMap.title')"
        :subtitle="subtitle"
        :filters="filters"
        :actions="actions"
      >
        <template #filters-extra>
          <!-- Hide-unnamed toggle. ml-auto pushes it to the far end
               of the filter row so it visually separates from the
               data filters on the left. The icon flips to mirror
               the persisted state. -->
          <button
            type="button"
            class="ml-auto inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md border border-default bg-default hover:bg-elevated text-muted hover:text-default text-xs transition-colors"
            :title="hideUnnamed ? t('serviceMap.showUnnamed') : t('serviceMap.hideUnnamed')"
            :aria-label="hideUnnamed ? t('serviceMap.showUnnamed') : t('serviceMap.hideUnnamed')"
            @click="hideUnnamed = !hideUnnamed"
          >
            <UIcon
              :name="hideUnnamed ? 'i-ph-eye' : 'i-ph-eye-slash'"
              class="size-3.5"
            />
          </button>
        </template>
      </AppToolbar>
    </template>

    <UAlert
      v-if="page.error.value"
      color="error"
      variant="subtle"
      icon="i-ph-warning"
      :title="page.error.value"
      class="mb-4"
    />

    <AppEmptyState
      v-if="isEmpty"
      icon="i-ph-graph"
      :title="t('serviceMap.emptyTitle')"
      :description="t('serviceMap.emptyDescription')"
    />

    <ServiceMapGraph
      v-else
      :data="resolvedData"
      :selected="page.selected.value"
      @select="(s) => page.selected.value = s"
    />

    <ServiceDetailDrawer
      :service="page.selected.value"
      :data="displayedData"
      :range="page.range.value"
      @close="page.selected.value = null"
    />
  </AppPage>
</template>
