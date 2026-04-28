import { computed, ref, watch } from 'vue'
import { useLivePolling } from '~/composables/useLivePolling'
import type { MetricsService } from '~/services/MetricsService'
import type { InstrumentDto, MetricSeriesDto } from '~/services/types'

/**
 * Two-state page model: the instrument list (reloaded on demand) and the
 * currently-selected series (reloaded whenever the selection changes). Pure
 * Vue refs — no cache — because the in-memory backend is cheap to poll and
 * the caller controls reload cadence.
 *
 * Live mode re-reads BOTH the list and the selected series silently (without
 * flashing the Loading… row), since the in-memory ring buffer is already the
 * source of truth — there's no "append" semantic for metrics.
 *
 * Application filter semantics (as requested): no "All" option. If exactly
 * one application is currently producing metrics, auto-select it on load;
 * otherwise the user must pick one before any instruments are shown.
 */
export function useMetricsPage(service: MetricsService) {
  const instruments = ref<InstrumentDto[]>([])
  const selected = ref<InstrumentDto | null>(null)
  const series = ref<MetricSeriesDto | null>(null)
  const availableServices = ref<string[]>([])
  const serviceFilter = ref<string | null>(null)
  const isLoadingList = ref(false)
  const isLoadingSeries = ref(false)
  const error = ref<string | null>(null)

  const filteredInstruments = computed(() => {
    if (!serviceFilter.value) return [] as InstrumentDto[]
    const target = serviceFilter.value
    return instruments.value.filter(i => i.serviceName === target)
  })

  async function reloadServices(silent = false) {
    try {
      availableServices.value = await service.listServices()
      // Auto-select when exactly one application is producing metrics and
      // the user hasn't made an explicit choice yet. Never auto-DEselects:
      // if the list grows to >1 we keep whatever the user had.
      if (serviceFilter.value === null && availableServices.value.length === 1) {
        serviceFilter.value = availableServices.value[0] ?? null
      }
      if (silent) error.value = null
    } catch (e) {
      if (!silent) error.value = e instanceof Error ? e.message : String(e)
    }
  }

  async function reloadList(silent = false) {
    if (!silent) isLoadingList.value = true
    if (!silent) error.value = null
    try {
      const list = await service.listInstruments()
      instruments.value = list
      // Drop the selection if it disappeared from the server snapshot.
      if (selected.value && !list.some(i => sameInstrument(i, selected.value!))) {
        selected.value = null
        series.value = null
      }
      if (silent) error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      if (!silent) isLoadingList.value = false
    }
  }

  async function reloadSeries(silent = false) {
    if (!selected.value) {
      series.value = null
      return
    }
    if (!silent) isLoadingSeries.value = true
    if (!silent) error.value = null
    try {
      const next = await service.getPoints({
        resourceHash: selected.value.resourceHash,
        scopeName: selected.value.scopeName,
        instrumentName: selected.value.name,
        kind: selected.value.kind
      })
      series.value = next
      if (silent) error.value = null
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
      if (!silent) series.value = null
    } finally {
      if (!silent) isLoadingSeries.value = false
    }
  }

  function selectInstrument(instrument: InstrumentDto) {
    selected.value = instrument
    reloadSeries()
  }

  async function liveTick() {
    await reloadServices(true)
    await reloadList(true)
    if (selected.value) await reloadSeries(true)
  }

  const live = useLivePolling(liveTick, { autoStart: true })

  // Changing the filter clears the selected series: the row may not be in
  // the new filtered view.
  watch(serviceFilter, (next, prev) => {
    if (next !== prev) {
      selected.value = null
      series.value = null
    }
  })

  void reloadServices()
  void reloadList()

  return {
    instruments: filteredInstruments,
    rawInstruments: instruments,
    selected,
    series,
    availableServices,
    service: serviceFilter,
    isLoadingList,
    isLoadingSeries,
    error,
    reloadList: () => reloadList(false),
    reloadSeries,
    select: selectInstrument,
    isLive: live.isLive,
    toggleLive: live.toggle
  }
}

function sameInstrument(a: InstrumentDto, b: InstrumentDto): boolean {
  return a.resourceHash === b.resourceHash
    && a.scopeName === b.scopeName
    && a.name === b.name
    && a.kind === b.kind
}
