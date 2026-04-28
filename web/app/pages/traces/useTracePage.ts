import type { TraceService } from '~/services/TraceService'
import type { SpanDto, TraceDetailDto } from '~/services/types'

export function useTracePage(service: TraceService, traceId: string) {
  const trace = ref<TraceDetailDto | null>(null)
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const selected = ref<SpanDto | null>(null)
  const notFound = ref(false)

  async function load() {
    isLoading.value = true
    error.value = null
    notFound.value = false
    try {
      trace.value = await service.getTrace(traceId)
    } catch (e: unknown) {
      // ofetch throws FetchError with `.statusCode`; 404 means trace not found.
      const fetchErr = e as { statusCode?: number, message?: string }
      if (fetchErr?.statusCode === 404) {
        notFound.value = true
      } else {
        error.value = fetchErr?.message ?? String(e)
      }
    } finally {
      isLoading.value = false
    }
  }

  load()

  return { trace, isLoading, error, notFound, selected }
}
