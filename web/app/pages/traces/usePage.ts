import { ref, watch } from 'vue'
import { useLivePolling } from '~/composables/useLivePolling'
import type { TraceService } from '~/services/TraceService'
import type { TimeWindow, TraceSummaryDto } from '~/services/types'
import type { DurationRange, TraceStatusFilter } from '~/types/filters'

const MAX_LIVE_ITEMS = 5000
const LIVE_DELTA_LIMIT = 500

export interface UseTracesPageOptions {
  /** Default: true. Set to false in unit tests to control live mode manually. */
  autoLive?: boolean
}

export function useTracesPage(service: TraceService, options: UseTracesPageOptions = {}) {
  const defaultWindow = (): TimeWindow => {
    const to = new Date()
    const from = new Date(to.getTime() - 60 * 60 * 1000)
    return { from: from.toISOString(), to: to.toISOString() }
  }

  const range = ref<TimeWindow>(defaultWindow())
  const serviceFilter = ref<string | null>(null)
  const availableServices = ref<string[]>([])
  const limit = ref(50)
  const items = ref<TraceSummaryDto[]>([])
  const cursor = ref<string | null>(null)
  const hasMore = ref(false)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  // Server-side filters owned by the composable so pagination is honest
  // about the filtered result set. Status uses 'any-span' semantics
  // server-side (matches the existing service filter): 'error' = trace
  // contains at least one Error span; 'ok' = no Error spans.
  const statusFilter = ref<TraceStatusFilter>('any')
  const durationFilter = ref<DurationRange>({ minMs: null, maxMs: null })
  const searchQuery = ref('')

  async function fetchPage(append: boolean) {
    isLoading.value = true
    error.value = null
    try {
      const response = await service.listTraces({
        from: range.value.from,
        to: range.value.to,
        limit: limit.value,
        cursor: append ? cursor.value ?? undefined : undefined,
        service: serviceFilter.value ?? undefined,
        status: statusFilter.value === 'any' ? undefined : statusFilter.value,
        minMs: durationFilter.value.minMs ?? undefined,
        maxMs: durationFilter.value.maxMs ?? undefined,
        spanNameContains: searchQuery.value.trim() || undefined
      })
      items.value = append ? [...items.value, ...response.items] : response.items
      cursor.value = response.nextCursor
      hasMore.value = response.nextCursor !== null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      isLoading.value = false
    }
  }

  async function liveTick() {
    const anchorIso = items.value[0]?.start ?? range.value.to
    const now = new Date().toISOString()

    try {
      const response = await service.listTraces({
        from: anchorIso,
        to: now,
        limit: LIVE_DELTA_LIMIT,
        service: serviceFilter.value ?? undefined,
        status: statusFilter.value === 'any' ? undefined : statusFilter.value,
        minMs: durationFilter.value.minMs ?? undefined,
        maxMs: durationFilter.value.maxMs ?? undefined,
        spanNameContains: searchQuery.value.trim() || undefined
      })

      if (response.items.length === 0) {
        error.value = null
        return
      }

      const indexByTraceId = new Map<string, number>()
      items.value.forEach((t, i) => indexByTraceId.set(t.traceId, i))

      const prepended: TraceSummaryDto[] = []
      let next: TraceSummaryDto[] | null = null
      for (const t of response.items) {
        const existing = indexByTraceId.get(t.traceId)
        if (existing !== undefined) {
          if (next === null) next = [...items.value]
          next[existing] = t
        } else {
          prepended.push(t)
        }
      }

      const base = next ?? items.value
      if (prepended.length > 0) {
        items.value = [...prepended, ...base]
        if (items.value.length > MAX_LIVE_ITEMS) {
          items.value = items.value.slice(0, MAX_LIVE_ITEMS)
        }
      } else if (next !== null) {
        items.value = next
      }
      error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  }

  async function loadServices() {
    try {
      availableServices.value = await service.listServices({
        from: range.value.from,
        to: range.value.to
      })
    } catch {
      /* keep previous list silent on transient errors */
    }
  }

  const live = useLivePolling(liveTick, { autoStart: options.autoLive ?? true })

  const reload = () => fetchPage(false)
  const loadMore = () => fetchPage(true)

  // Range / limit / service filter all trigger a reload — changing any
  // of them is the user asking for a different slice. The services list
  // also re-fetches on range change because the set of services seen
  // *in that window* may differ. Skipped while live mode is on (the
  // range / limit filters are UI-disabled then anyway).
  watch(() => [range.value.from, range.value.to], () => {
    void loadServices()
    if (!live.isLive.value) void reload()
  })
  watch(limit, () => {
    if (!live.isLive.value) void reload()
  })
  watch(serviceFilter, () => { void reload() })
  watch(statusFilter, () => {
    if (!live.isLive.value) void reload()
  })
  watch(() => [durationFilter.value.minMs, durationFilter.value.maxMs], () => {
    if (!live.isLive.value) void reload()
  })
  watch(searchQuery, () => {
    if (!live.isLive.value) void reload()
  })

  reload()
  void loadServices()

  return {
    range,
    limit,
    service: serviceFilter,
    availableServices,
    items,
    hasMore,
    isLoading,
    error,
    statusFilter,
    durationFilter,
    searchQuery,
    reload,
    loadMore,
    isLive: live.isLive,
    toggleLive: live.toggle
  }
}
