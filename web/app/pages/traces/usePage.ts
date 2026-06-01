import { computed, ref, watch } from 'vue'
import { useLivePolling } from '~/composables/useLivePolling'
import type { TraceService } from '~/services/TraceService'
import type { TimeWindow, TraceSummaryDto } from '~/services/types'
import type { DurationRange, TraceStatusFilter } from '~/types/filters'
import { isKnownPresetKey, presetToWindow } from '~/lib/timeRangePresets'

const MAX_LIVE_ITEMS = 5000
const LIVE_DELTA_LIMIT = 500
const DEFAULT_LIMIT = 50

export interface UseTracesPageOptions {
  initialRange?: TimeWindow
  /** Rolling preset key (e.g. `'1h'`). When set, the window is
   *  recomputed relative to `now` on hydration — so back-navigation
   *  from a trace detail returns to "now - 1h" rather than the frozen
   *  pair of timestamps the URL might still carry. `initialRange` is
   *  ignored in that case. */
  initialPreset?: string | null
  initialServices?: string[]
  /** When true, the user has explicitly deselected every application
   *  in the picker — the page short-circuits to an empty list rather
   *  than the "no filter = all" fallback. Persisted as
   *  `noApplications=true` in the URL. */
  initialNoApplications?: boolean
  /** When true, restrict the listing to traces involving Resources
   *  with no `service.name` (null or empty). Drives the service-map's
   *  "(unnamed)" drill-down. Mutually exclusive with `initialService`. */
  initialNoService?: boolean
  /** Initial service-match anchor. Defaults to `'root'` (the
   *  intuitive UI default); pages restore `'any'` from the URL when
   *  the user opted into the cross-service discovery semantics. */
  initialServiceMatch?: 'root' | 'any'
  initialStatus?: TraceStatusFilter
  initialDuration?: DurationRange
  initialSearch?: string
  initialAttr?: string[]
  initialLimit?: number
  /** When the user manually disabled live mode, the URL persists
   *  `live=false` so the choice survives navigation away and back.
   *  Default: true (auto-start on a fresh visit). */
  initialLive?: boolean
  /** Override `initialLive` for unit tests where the timer-driven
   *  polling gets in the way. Default: undefined (use initialLive). */
  autoLive?: boolean
}

export function useTracesPage(service: TraceService, options: UseTracesPageOptions = {}) {
  const defaultWindow = (): TimeWindow => {
    const to = new Date()
    const from = new Date(to.getTime() - 60 * 60 * 1000)
    return { from: from.toISOString(), to: to.toISOString() }
  }

  // Three hydration branches:
  //  1. URL has a known `?range=` → rolling preset wins, even if stale
  //     `?from=&to=` is also present (that's exactly how back-nav gets
  //     a fresh window).
  //  2. URL has explicit `?from=&to=` → custom absolute window, no
  //     preset; shareable as a static snapshot.
  //  3. URL has neither → default to the rolling `1h` preset. Without
  //     this branch a fresh visit would persist absolute timestamps and
  //     back-nav from a trace detail would land on a frozen window.
  const rangePreset = ref<string | null>(
    isKnownPresetKey(options.initialPreset)
      ? options.initialPreset
      : (options.initialRange ? null : '1h')
  )
  const initialWindow = rangePreset.value
    ? presetToWindow(rangePreset.value)
    : (options.initialRange ?? defaultWindow())
  const range = ref<TimeWindow>(initialWindow)
  // Multi-value allow-list for `service.name`. Empty array combined
  // with `noApplications === false` means "all applications" — the
  // convention used by the picker and the server-side `services=` URL
  // param. `noApplications === true` is the literal "user deselected
  // every box" state; the fetch is short-circuited in that branch.
  const serviceFilter = ref<string[]>(options.initialServices ?? [])
  const noApplications = ref<boolean>(options.initialNoApplications === true)
  const serviceMatch = ref<'root' | 'any'>(options.initialServiceMatch ?? 'root')
  const noServiceFilter = ref<boolean>(options.initialNoService === true)
  const availableServices = ref<string[]>([])
  const limit = ref(options.initialLimit ?? DEFAULT_LIMIT)
  const items = ref<TraceSummaryDto[]>([])
  const cursor = ref<string | null>(null)
  const hasMore = ref(false)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  // Server-side filters owned by the composable so pagination is honest
  // about the filtered result set. Status uses 'any-span' semantics
  // server-side (matches the existing service filter): 'error' = trace
  // contains at least one Error span; 'ok' = no Error spans.
  const statusFilter = ref<TraceStatusFilter>(options.initialStatus ?? 'any')
  const durationFilter = ref<DurationRange>(options.initialDuration ?? { minMs: null, maxMs: null })
  const searchQuery = ref(options.initialSearch ?? '')
  const attributeFilters = ref<string[]>(options.initialAttr ?? [])

  async function fetchPage(append: boolean) {
    // "Deselected all" short-circuit: no application selected means no
    // rows to show — skip the round-trip rather than send a filter the
    // server would interpret as "no filter".
    if (noApplications.value) {
      if (!append) {
        items.value = []
        cursor.value = null
        hasMore.value = false
      }
      isLoading.value = false
      error.value = null
      return
    }
    isLoading.value = true
    error.value = null
    try {
      const response = await service.listTraces({
        from: range.value.from,
        to: range.value.to,
        limit: limit.value,
        cursor: append ? cursor.value ?? undefined : undefined,
        services: noServiceFilter.value
          ? undefined
          : (serviceFilter.value.length > 0 ? serviceFilter.value : undefined),
        serviceMatch: serviceMatch.value === 'any' ? 'any' : undefined,
        noService: noServiceFilter.value || undefined,
        status: statusFilter.value === 'any' ? undefined : statusFilter.value,
        minMs: durationFilter.value.minMs ?? undefined,
        maxMs: durationFilter.value.maxMs ?? undefined,
        spanNameContains: searchQuery.value.trim() || undefined,
        attr: attributeFilters.value.length > 0 ? attributeFilters.value : undefined
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
    // Mirror the fetchPage short-circuit: with no applications
    // selected, the live tail has nothing to fetch.
    if (noApplications.value) {
      error.value = null
      return
    }
    const anchorIso = items.value[0]?.start ?? range.value.to
    const now = new Date().toISOString()

    try {
      const response = await service.listTraces({
        from: anchorIso,
        to: now,
        limit: LIVE_DELTA_LIMIT,
        services: noServiceFilter.value
          ? undefined
          : (serviceFilter.value.length > 0 ? serviceFilter.value : undefined),
        serviceMatch: serviceMatch.value === 'any' ? 'any' : undefined,
        noService: noServiceFilter.value || undefined,
        status: statusFilter.value === 'any' ? undefined : statusFilter.value,
        minMs: durationFilter.value.minMs ?? undefined,
        maxMs: durationFilter.value.maxMs ?? undefined,
        spanNameContains: searchQuery.value.trim() || undefined,
        attr: attributeFilters.value.length > 0 ? attributeFilters.value : undefined
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
        // Newly streamed traces may belong to a service that connected
        // after page load — refresh the picker against the live `now`
        // window so the Applications filter picks it up.
        void loadServices(now)
      } else if (next !== null) {
        items.value = next
      }
      error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  }

  // `toOverride` lets the live tail discover services that connected
  // after the page loaded: their spans sit past the frozen `range.to`,
  // so the picker must be refreshed against a live `now` boundary or
  // they never surface in the Applications filter.
  async function loadServices(toOverride?: string) {
    try {
      availableServices.value = await service.listServices({
        from: range.value.from,
        to: toOverride ?? range.value.to
      })
    } catch {
      /* keep previous list silent on transient errors */
    }
  }

  const live = useLivePolling(liveTick, {
    autoStart: options.autoLive ?? options.initialLive ?? true
  })

  const reload = () => fetchPage(false)
  const loadMore = () => fetchPage(true)

  // Range and limit are the only filters whose UI is *disabled* while
  // live mode is on (the live tail can't honour either without
  // re-querying the whole window), so their reload stays gated. The
  // rest stay interactive in live mode — and must reload immediately
  // on change, otherwise the user sees the picker move but the rows
  // don't filter. Subsequent live ticks compose on top of the
  // already-filtered list.
  watch(() => [range.value.from, range.value.to], () => {
    void loadServices()
    if (!live.isLive.value) void reload()
  })
  watch(limit, () => {
    if (!live.isLive.value) void reload()
  })
  watch(serviceFilter, () => { void reload() }, { deep: true })
  watch(noApplications, () => { void reload() })
  watch(noServiceFilter, () => { void reload() })
  watch(serviceMatch, () => { void reload() })
  watch(statusFilter, () => { void reload() })
  watch(() => [durationFilter.value.minMs, durationFilter.value.maxMs], () => { void reload() })
  watch(searchQuery, () => { void reload() })
  watch(attributeFilters, () => { void reload() }, { deep: true })

  // Filter state encoded for URL persistence — see logs/usePage for
  // the rationale. Defaulted values are omitted to keep the URL short.
  //
  // Rolling presets serialise as `range=1h` (no from/to) so a round-
  // trip through `/traces/{id}` and back recomputes the window from
  // `now` — without that, the absolute timestamps would freeze and
  // Live Mode would resume from a stale window.
  const queryState = computed(() => {
    const q: Record<string, string | string[]> = {}
    if (rangePreset.value) {
      q.range = rangePreset.value
    } else {
      q.from = range.value.from
      q.to = range.value.to
    }
    if (noServiceFilter.value) q.noService = 'true'
    else if (noApplications.value) q.noApplications = 'true'
    else if (serviceFilter.value.length > 0) q.services = serviceFilter.value.join(',')
    if (serviceMatch.value === 'any') q.serviceMatch = 'any'
    if (statusFilter.value !== 'any') q.status = statusFilter.value
    if (durationFilter.value.minMs != null) q.minMs = String(durationFilter.value.minMs)
    if (durationFilter.value.maxMs != null) q.maxMs = String(durationFilter.value.maxMs)
    const search = searchQuery.value.trim()
    if (search) q.spanNameContains = search
    if (attributeFilters.value.length > 0) q.attr = attributeFilters.value
    if (limit.value !== DEFAULT_LIMIT) q.limit = String(limit.value)
    // Live mode default is on; only encode the negative state so a
    // fresh visit auto-starts as before. Surviving the round-trip
    // through `/traces/{id}` and back is the user-facing point.
    if (!live.isLive.value) q.live = 'false'
    return q
  })

  reload()
  void loadServices()

  return {
    range,
    rangePreset,
    limit,
    service: serviceFilter,
    noApplications,
    noService: noServiceFilter,
    serviceMatch,
    availableServices,
    items,
    hasMore,
    isLoading,
    error,
    statusFilter,
    durationFilter,
    searchQuery,
    attributeFilters,
    queryState,
    reload,
    loadMore,
    isLive: live.isLive,
    toggleLive: live.toggle
  }
}
