import { computed, ref, watch } from 'vue'
import { useLivePolling } from '~/composables/useLivePolling'
import type { LogsService } from '~/services/LogsService'
import type { LogRecordDto, TimeWindow } from '~/services/types'
import type { SeverityBucket } from '~/types/filters'
import { isKnownPresetKey, presetToWindow } from '~/lib/timeRangePresets'

export interface UseLogsPageOptions {
  initialRange?: TimeWindow
  /** Rolling preset key (e.g. `'1h'`). When set, the window is
   *  recomputed relative to `now` on hydration — so back-navigation
   *  from a log detail returns to "now - 1h" rather than the frozen
   *  pair of timestamps the URL might still carry. `initialRange` is
   *  ignored in that case. */
  initialPreset?: string | null
  initialTraceId?: string
  initialServices?: string[]
  /** When true, the user has explicitly deselected every application
   *  in the picker — the page short-circuits to an empty list rather
   *  than the "no filter = all" fallback. Persisted as
   *  `noApplications=true` in the URL. */
  initialNoApplications?: boolean
  initialSeverity?: SeverityBucket[]
  initialBody?: string
  initialAttr?: string[]
  initialLimit?: number
  /** Persisted in the URL as `live=false` once the user toggles
   *  off, so the choice survives navigation away and back.
   *  Default: true. */
  initialLive?: boolean
  /** Override `initialLive` for unit tests. */
  autoLive?: boolean
}

const DEFAULT_LIMIT = 50

// Cap on rows retained while live — older ones are trimmed from the tail.
const MAX_LIVE_ITEMS = 5000

// Per-tick delta fetch limit. High enough to cover bursts, low enough to
// bound bandwidth on a noisy feed.
const LIVE_DELTA_LIMIT = 500

/**
 * State + fetch orchestration for the logs page.
 * The page itself stays declarative; this composable owns "what happens when
 * the user clicks reload / load-more / picks a row / toggles live". Optional
 * initial values let the page bootstrap from the URL query string (e.g. when
 * navigated from the trace detail via "View Logs").
 */
export function useLogsPage(service: LogsService, options: UseLogsPageOptions = {}) {
  const defaultWindow = (): TimeWindow => {
    const to = new Date()
    const from = new Date(to.getTime() - 60 * 60 * 1000) // last 1h
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
  //     back-nav from a log detail would land on a frozen window.
  const rangePreset = ref<string | null>(
    isKnownPresetKey(options.initialPreset)
      ? options.initialPreset
      : (options.initialRange ? null : '1h')
  )
  const initialWindow = rangePreset.value
    ? presetToWindow(rangePreset.value)
    : (options.initialRange ?? defaultWindow())
  const range = ref<TimeWindow>(initialWindow)
  const traceId = ref<string | undefined>(options.initialTraceId)
  // Multi-value allow-list for `service.name`. Empty array combined
  // with `noApplications === false` is the canonical "no filter"
  // state — same convention used by the severity picker and the
  // `services=` URL param. `noApplications === true` is the literal
  // "user deselected every box" state; the fetch is short-circuited
  // in that branch.
  const serviceFilter = ref<string[]>(options.initialServices ?? [])
  const noApplications = ref<boolean>(options.initialNoApplications === true)
  const availableServices = ref<string[]>([])
  const limit = ref(options.initialLimit ?? DEFAULT_LIMIT)
  const items = ref<LogRecordDto[]>([])
  const cursor = ref<string | null>(null)
  const hasMore = ref(false)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const selected = ref<LogRecordDto | null>(null)
  // Server-side filters owned by the composable so the API call stays in
  // one place and pagination is honest about the filtered result set
  // (frontend-only filters used to filter the loaded page only — selective
  // filters then hid every match outside the first page).
  const severityFilter = ref<SeverityBucket[]>(options.initialSeverity ?? [])
  const bodyQuery = ref(options.initialBody ?? '')
  const attributeFilters = ref<string[]>(options.initialAttr ?? [])

  // Client-side dedup for live-mode prepends. Log records have no stable ID
  // server-side, so we key on (time, spanId, body-prefix) — collisions are
  // vanishingly rare in practice and a stray duplicate is cosmetic only.
  const seenKeys = new Set<string>()
  function keyOf(r: LogRecordDto): string {
    return `${r.time}|${r.spanId ?? ''}|${(r.body ?? '').slice(0, 64)}`
  }

  async function fetchPage(append: boolean) {
    // "Deselected all" short-circuit: no application selected means no
    // rows to show — skip the round-trip rather than send a filter the
    // server would interpret as "no filter".
    if (noApplications.value) {
      if (!append) {
        items.value = []
        cursor.value = null
        hasMore.value = false
        seenKeys.clear()
      }
      isLoading.value = false
      error.value = null
      return
    }
    isLoading.value = true
    error.value = null
    try {
      const response = await service.listLogs({
        from: range.value.from,
        to: range.value.to,
        limit: limit.value,
        cursor: append ? cursor.value ?? undefined : undefined,
        traceId: traceId.value,
        services: serviceFilter.value.length > 0 ? serviceFilter.value : undefined,
        severities: severityFilter.value.length > 0 ? severityFilter.value : undefined,
        bodyContains: bodyQuery.value.trim() || undefined,
        attr: attributeFilters.value.length > 0 ? attributeFilters.value : undefined
      })

      if (append) {
        items.value = [...items.value, ...response.items]
      } else {
        items.value = response.items
        seenKeys.clear()
      }
      for (const r of response.items) seenKeys.add(keyOf(r))
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
    // Live-tail anchor: newest row we've already shown (items are DESC by
    // time, so items[0] is the newest). Fall back to range.to on empty
    // screen so the first tick doesn't refetch the whole window.
    const anchorIso = items.value[0]?.time ?? range.value.to
    const now = new Date().toISOString()

    try {
      const response = await service.listLogs({
        from: anchorIso,
        to: now,
        limit: LIVE_DELTA_LIMIT,
        traceId: traceId.value,
        services: serviceFilter.value.length > 0 ? serviceFilter.value : undefined,
        severities: severityFilter.value.length > 0 ? severityFilter.value : undefined,
        bodyContains: bodyQuery.value.trim() || undefined,
        attr: attributeFilters.value.length > 0 ? attributeFilters.value : undefined
      })

      const fresh: LogRecordDto[] = []
      for (const r of response.items) {
        const k = keyOf(r)
        if (seenKeys.has(k)) continue
        seenKeys.add(k)
        fresh.push(r)
      }

      if (fresh.length > 0) {
        // Server returns DESC by time → prepend preserves overall ordering.
        items.value = [...fresh, ...items.value]
        if (items.value.length > MAX_LIVE_ITEMS) {
          for (const dropped of items.value.slice(MAX_LIVE_ITEMS)) {
            seenKeys.delete(keyOf(dropped))
          }
          items.value = items.value.slice(0, MAX_LIVE_ITEMS)
        }
        // Newly streamed logs may belong to a service that connected
        // after page load — refresh the picker against the live `now`
        // window so the Applications filter picks it up.
        void loadServices(now)
      }
      error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    }
  }

  // `toOverride` lets the live tail discover services that connected
  // after the page loaded: their logs sit past the frozen `range.to`,
  // so the picker must be refreshed against a live `now` boundary or
  // they never surface in the Applications filter.
  async function loadServices(toOverride?: string) {
    try {
      availableServices.value = await service.listServices({
        from: range.value.from,
        to: toOverride ?? range.value.to
      })
    } catch {
      // Keep the previous list silent on transient errors so the filter
      // doesn't flicker.
    }
  }

  const live = useLivePolling(liveTick, {
    autoStart: options.autoLive ?? options.initialLive ?? true
  })

  const reload = () => fetchPage(false)
  const loadMore = () => fetchPage(true)

  // Range and limit are UI-disabled in live mode (the live tail
  // can't shift them without re-querying), so their reload stays
  // gated. Every other filter is interactive in live mode and must
  // reload immediately on change — the user toggling severity sees
  // the rows filter right away; subsequent live ticks compose on top.
  watch(() => [range.value.from, range.value.to], () => {
    void loadServices()
    if (!live.isLive.value) void reload()
  })
  watch(limit, () => {
    if (!live.isLive.value) void reload()
  })
  watch(serviceFilter, () => { void reload() }, { deep: true })
  watch(noApplications, () => { void reload() })
  watch(severityFilter, () => { void reload() }, { deep: true })
  // Body search reloads on each keystroke. The /v1/logs request is
  // light enough that user-perceived latency stays low; if this proves
  // chatty we'll add a debounce here.
  watch(bodyQuery, () => { void reload() })
  watch(attributeFilters, () => { void reload() }, { deep: true })

  // Filter state encoded for URL persistence. The page watches this
  // and pushes it back via `router.replace` so refreshes / shares /
  // bookmarks land on the same view. Optional (defaulted) values are
  // omitted to keep the URL short — the parser on the receiving end
  // falls back to the same defaults.
  //
  // Rolling presets serialise as `range=1h` (no from/to) so a round-
  // trip through `/logs/{id}` and back recomputes the window from
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
    if (traceId.value) q.traceId = traceId.value
    if (noApplications.value) q.noApplications = 'true'
    else if (serviceFilter.value.length > 0) q.services = serviceFilter.value.join(',')
    if (severityFilter.value.length > 0) q.severities = severityFilter.value.join(',')
    const body = bodyQuery.value.trim()
    if (body) q.bodyContains = body
    if (attributeFilters.value.length > 0) q.attr = attributeFilters.value
    if (limit.value !== DEFAULT_LIMIT) q.limit = String(limit.value)
    // Encode only the off state so a fresh visit auto-starts live;
    // turning it off and bouncing through `/logs/{id}` keeps the
    // user's choice via the URL.
    if (!live.isLive.value) q.live = 'false'
    return q
  })

  // Initial load.
  reload()
  void loadServices()

  return {
    range,
    rangePreset,
    limit,
    traceId,
    service: serviceFilter,
    noApplications,
    availableServices,
    items,
    hasMore,
    isLoading,
    error,
    selected,
    severityFilter,
    bodyQuery,
    attributeFilters,
    queryState,
    reload,
    loadMore,
    isLive: live.isLive,
    toggleLive: live.toggle
  }
}
