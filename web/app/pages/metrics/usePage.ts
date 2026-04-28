import type { AgChartOptions } from 'ag-charts-community'
import { useLivePolling } from '~/composables/useLivePolling'
import type { MetricsService } from '~/services/MetricsService'
import type {
  InstrumentDto,
  MetricPointDto,
  MetricSeriesDto,
  TimeWindow
} from '~/services/types'
import {
  buildTree,
  filterTree,
  instrumentKey,
  type MetricTreeNode
} from './buildTree'
import {
  buildChartOptions,
  distinctUnits,
  pickChartType,
  sharedUnit,
  type ChartType
} from './chartStrategy'
import {
  availableAttributeKeys,
  type SplitBy
} from './seriesGrouping'
import { useMetricsSelection } from './useMetricsSelection'

/**
 * Two-state page model for the metrics view:
 *  1. The full instrument list (rebuilt into a tree rooted by service.name);
 *  2. The series for the user's multi-selection (reloaded in parallel and
 *     merged on live ticks, never wiped — the chart should not flicker).
 *
 * No application filter: every service becomes a top-level branch in the tree
 * so the user sees the whole picture at once and can drill in. Multi-selection
 * is constrained to a single `kind` so the chart picks one representation; the
 * constraint is enforced inside `useMetricsSelection`.
 */
export function useMetricsPage(service: MetricsService) {
  const { t: _t } = useI18n()
  const { locale } = useI18n()
  const colorMode = useColorMode()

  // List + selection state
  const instruments = ref<InstrumentDto[]>([])
  const searchQuery = ref('')
  const range = ref<TimeWindow>(defaultRange())

  const series = ref<Map<string, MetricSeriesDto>>(new Map())
  const splitBy = ref<SplitBy>('all')

  const isLoadingList = ref(false)
  const isLoadingSeries = ref(false)
  const error = ref<string | null>(null)

  const selection = useMetricsSelection()

  // Derived: namespace tree, rooted by service.name and filtered by search
  const tree = computed<MetricTreeNode[]>(() => {
    const built = buildTree(instruments.value)
    return filterTree(built, searchQuery.value)
  })

  // Derived: attribute keys available across the loaded series (for split-by)
  const availableAttributes = computed<string[]>(() => availableAttributeKeys(series.value.values()))

  // Derived: chart type from the leading selected instrument (all selected
  // share `kind` thanks to the selection guard, so the leading one's
  // temporality/monotonic is representative).
  const chartType = computed<ChartType>(() => {
    const head = selection.selectedInstruments.value[0]
    if (!head) return 'line'
    return pickChartType(head.kind, head.temporality, head.isMonotonic)
  })

  const chartOptions = computed<AgChartOptions>(() => {
    const ordered: MetricSeriesDto[] = []
    for (const key of selection.selectedKeys.value) {
      const s = series.value.get(key)
      if (s) ordered.push(s)
    }
    return buildChartOptions({
      series: ordered,
      chartType: chartType.value,
      splitBy: splitBy.value,
      locale: locale.value,
      isDark: colorMode.value === 'dark'
    })
  })

  const selectedUnit = computed<string | null>(
    () => sharedUnit(selection.selectedInstruments.value)
  )
  const selectedUnits = computed<(string | null)[]>(
    () => distinctUnits(selection.selectedInstruments.value)
  )

  async function reloadList(silent = false) {
    if (!silent) isLoadingList.value = true
    if (!silent) error.value = null
    try {
      instruments.value = await service.listInstruments()
      selection.reconcile(instruments.value)
      if (silent) error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      if (!silent) isLoadingList.value = false
    }
  }

  async function reloadSeries(opts: { silent?: boolean } = {}) {
    const silent = opts.silent ?? false
    const keys = [...selection.selectedKeys.value]
    if (keys.length === 0) {
      series.value = new Map()
      return
    }
    if (!silent) isLoadingSeries.value = true
    if (!silent) error.value = null

    // In live mode slide the window forward instead of asking for the entire
    // ringbuffer: keep the same duration the user picked, but anchor `to` to
    // `now`. The API requires both bounds (and `to > from`), so we always
    // emit a fresh `to`; points emitted between request build and server
    // execution will land in the next tick a few seconds later.
    const isLive = live.isLive.value
    const sliding = isLive ? slidingWindow() : null
    const fromQ = sliding?.from ?? range.value.from
    const toQ = sliding?.to ?? range.value.to

    const tasks = keys.map(async (key) => {
      const snap = selection.selectedInstruments.value.find(i => instrumentKey(i) === key)
      if (!snap) return null
      try {
        const next = await service.getPoints({
          resourceHash: snap.resourceHash,
          scopeName: snap.scopeName,
          instrumentName: snap.name,
          kind: snap.kind,
          from: fromQ,
          to: toQ
        })
        return { key, series: next }
      } catch (e) {
        if (!silent) {
          error.value = e instanceof Error ? e.message : String(e)
        }
        return null
      }
    })

    const results = await Promise.all(tasks)
    const next = new Map<string, MetricSeriesDto>()

    for (const r of results) {
      if (!r) continue
      if (silent) {
        // Live tick: merge points into existing series, dedup by (time, attrs).
        const prev = series.value.get(r.key)
        next.set(r.key, prev ? mergeSeries(prev, r.series) : r.series)
      } else {
        next.set(r.key, r.series)
      }
    }
    // Preserve series for keys that are still selected but failed to refresh.
    if (silent) {
      for (const key of keys) {
        if (!next.has(key)) {
          const prev = series.value.get(key)
          if (prev) next.set(key, prev)
        }
      }
    }
    series.value = next
    if (!silent) isLoadingSeries.value = false
    if (silent) error.value = null
  }

  async function liveTick() {
    await reloadList(true)
    if (selection.selectedKeys.value.size > 0) {
      await reloadSeries({ silent: true })
    }
  }

  const live = useLivePolling(liveTick, { autoStart: true })

  // Selection / range / live-toggle all demand a refetch — `reloadSeries`
  // itself decides whether to apply the range as-is or slide it forward.
  watch(
    () => [...selection.selectedKeys.value].join(','),
    () => { void reloadSeries() }
  )
  watch(range, () => {
    if (selection.selectedKeys.value.size > 0 && !live.isLive.value) {
      void reloadSeries()
    }
  })
  watch(() => live.isLive.value, () => {
    if (selection.selectedKeys.value.size > 0) {
      void reloadSeries()
    }
  })

  function slidingWindow(): { from: string; to: string } {
    const fromMs = new Date(range.value.from).getTime()
    const toMs = new Date(range.value.to).getTime()
    // Guard against missing or inverted bounds; default to a 1h window.
    const duration = Number.isFinite(fromMs) && Number.isFinite(toMs) && toMs > fromMs
      ? toMs - fromMs
      : 60 * 60 * 1000
    const now = Date.now()
    return {
      from: new Date(now - duration).toISOString(),
      to: new Date(now).toISOString()
    }
  }

  // Initial fetch
  void reloadList()

  function toggleSelection(instrument: InstrumentDto) {
    selection.toggle(instrument)
  }

  return {
    // List
    instruments,
    tree,
    searchQuery,
    range,

    // Selection
    selectedKeys: selection.selectedKeys,
    selectedKind: selection.selectedKind,
    selectedInstruments: selection.selectedInstruments,
    selectedUnit,
    selectedUnits,
    isSelected: selection.isSelected,
    isCompatible: selection.isCompatible,
    toggleSelection,
    removeSelection: selection.remove,
    clearSelection: selection.clear,

    // Series + chart
    series,
    splitBy,
    availableAttributes,
    chartType,
    chartOptions,

    // State
    isLoadingList,
    isLoadingSeries,
    error,

    // Actions
    reloadList: () => reloadList(false),
    reloadSeries: () => reloadSeries(),
    isLive: live.isLive,
    toggleLive: live.toggle
  }
}

function defaultRange(): TimeWindow {
  const to = new Date()
  const from = new Date(to.getTime() - 60 * 60 * 1000) // last hour
  return { from: from.toISOString(), to: to.toISOString() }
}

function pointDedupKey(p: MetricPointDto): string {
  const attrs = Object.entries(p.attributes)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([k, v]) => `${k}=${formatValue(v)}`)
    .join('|')
  return `${p.time}|${attrs}`
}

function formatValue(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean') return String(v)
  return JSON.stringify(v)
}

function mergeSeries(prev: MetricSeriesDto, next: MetricSeriesDto): MetricSeriesDto {
  const seen = new Set<string>()
  const merged: MetricPointDto[] = []
  for (const p of prev.points) {
    const k = pointDedupKey(p)
    if (seen.has(k)) continue
    seen.add(k)
    merged.push(p)
  }
  for (const p of next.points) {
    const k = pointDedupKey(p)
    if (seen.has(k)) continue
    seen.add(k)
    merged.push(p)
  }
  // The chart strategy will sort per-group on render — here we keep arrival
  // order so the live tail appends naturally.
  return { instrument: next.instrument, points: merged }
}
