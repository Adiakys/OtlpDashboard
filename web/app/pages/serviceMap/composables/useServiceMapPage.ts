import { ref, watch } from 'vue'
import type { ServiceMapService } from '~/services/ServiceMapService'
import type { ServiceMapDto, TimeWindow } from '~/services/types'

const EMPTY_MAP: ServiceMapDto = { nodes: [], edges: [] }

/**
 * Page-level state for /service-map. Owns the time range, the optional
 * focus service, and the fetch lifecycle. Refresh is manual: the
 * service map is exploratory, not a live monitor — auto-ticking the
 * graph layout every 5s would be disorienting.
 */
export function useServiceMapPage(serviceMapService: ServiceMapService) {
  const defaultWindow = (): TimeWindow => {
    const to = new Date()
    const from = new Date(to.getTime() - 60 * 60 * 1000)
    return { from: from.toISOString(), to: to.toISOString() }
  }

  const range = ref<TimeWindow>(defaultWindow())
  const serviceFilter = ref<string | null>(null)
  const data = ref<ServiceMapDto>(EMPTY_MAP)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const selected = ref<string | null>(null)

  let inFlight = 0

  async function reload() {
    const ticket = ++inFlight
    isLoading.value = true
    error.value = null
    try {
      const response = await serviceMapService.getServiceMap({
        from: range.value.from,
        to: range.value.to,
        service: serviceFilter.value
      })
      if (ticket !== inFlight) return
      data.value = response
    } catch (e) {
      if (ticket === inFlight) {
        error.value = e instanceof Error ? e.message : String(e)
      }
    } finally {
      if (ticket === inFlight) isLoading.value = false
    }
  }

  // Reload on range / focus change. The user's "selected" node only
  // closes the drawer if the underlying service is gone — otherwise
  // we keep the side panel open so a refresh feels stable.
  watch(() => [range.value.from, range.value.to, serviceFilter.value], () => {
    void reload()
  })

  reload()

  return {
    range,
    serviceFilter,
    data,
    isLoading,
    error,
    selected,
    reload
  }
}
