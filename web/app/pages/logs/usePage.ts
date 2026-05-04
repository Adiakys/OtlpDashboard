import { ref, watch } from 'vue'
import { useLivePolling } from '~/composables/useLivePolling'
import type { LogsService } from '~/services/LogsService'
import type { LogRecordDto, TimeWindow } from '~/services/types'

export interface UseLogsPageOptions {
  initialRange?: TimeWindow
  initialTraceId?: string
  /** Default: true. Set to false in unit tests to control live mode manually. */
  autoLive?: boolean
}

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

  const range = ref<TimeWindow>(options.initialRange ?? defaultWindow())
  const traceId = ref<string | undefined>(options.initialTraceId)
  const serviceFilter = ref<string | null>(null)
  const availableServices = ref<string[]>([])
  const limit = ref(50)
  const items = ref<LogRecordDto[]>([])
  const cursor = ref<string | null>(null)
  const hasMore = ref(false)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const selected = ref<LogRecordDto | null>(null)

  // Client-side dedup for live-mode prepends. Log records have no stable ID
  // server-side, so we key on (time, spanId, body-prefix) — collisions are
  // vanishingly rare in practice and a stray duplicate is cosmetic only.
  const seenKeys = new Set<string>()
  function keyOf(r: LogRecordDto): string {
    return `${r.time}|${r.spanId ?? ''}|${(r.body ?? '').slice(0, 64)}`
  }

  async function fetchPage(append: boolean) {
    isLoading.value = true
    error.value = null
    try {
      const response = await service.listLogs({
        from: range.value.from,
        to: range.value.to,
        limit: limit.value,
        cursor: append ? cursor.value ?? undefined : undefined,
        traceId: traceId.value,
        service: serviceFilter.value ?? undefined
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
        service: serviceFilter.value ?? undefined
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
      // Keep the previous list silent on transient errors so the filter
      // doesn't flicker.
    }
  }

  const live = useLivePolling(liveTick, { autoStart: options.autoLive ?? true })

  const reload = () => fetchPage(false)
  const loadMore = () => fetchPage(true)

  // Range / limit / service filter all trigger a reload of the table —
  // changing any of them is the user saying "show me a different slice".
  // The services list also re-fetches on range change because the set of
  // services seen *in that window* may differ. Skipped while live mode
  // is on (the range / limit filters are UI-disabled then anyway).
  watch(() => [range.value.from, range.value.to], () => {
    void loadServices()
    if (!live.isLive.value) void reload()
  })
  watch(limit, () => {
    if (!live.isLive.value) void reload()
  })
  watch(serviceFilter, () => { void reload() })

  // Initial load.
  reload()
  void loadServices()

  return {
    range,
    limit,
    traceId,
    service: serviceFilter,
    availableServices,
    items,
    hasMore,
    isLoading,
    error,
    selected,
    reload,
    loadMore,
    isLive: live.isLive,
    toggleLive: live.toggle
  }
}
