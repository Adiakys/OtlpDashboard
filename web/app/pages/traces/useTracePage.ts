import type { LogsService } from '~/services/LogsService'
import type { TraceService } from '~/services/TraceService'
import type { LogRecordDto, SpanDto, TraceDetailDto } from '~/services/types'

export function useTracePage(
  traceService: TraceService,
  logsService: LogsService,
  traceId: string
) {
  const trace = ref<TraceDetailDto | null>(null)
  const logs = ref<LogRecordDto[]>([])
  const isLoading = ref(false)
  const error = ref<string | null>(null)
  const selected = ref<SpanDto | null>(null)
  const notFound = ref(false)

  async function load() {
    isLoading.value = true
    error.value = null
    notFound.value = false
    logs.value = []
    try {
      trace.value = await traceService.getTrace(traceId)
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

    // Once the trace is loaded, fan out a second request for the
    // correlated logs. Done sequentially so the from/to window can be
    // pinned to the trace's actual extent (with a 60s buffer either
    // side to catch logs emitted just before the root span starts or
    // just after it ends). Failures are non-fatal — span markers are
    // a nice-to-have, the trace itself renders without them.
    const t = trace.value
    if (!t || t.spans.length === 0) return
    let minStart = Number.POSITIVE_INFINITY
    let maxEnd = Number.NEGATIVE_INFINITY
    for (const s of t.spans) {
      const start = new Date(s.start).getTime()
      const end = new Date(s.end).getTime()
      if (start < minStart) minStart = start
      if (end > maxEnd) maxEnd = end
    }
    try {
      const response = await logsService.listLogs({
        traceId,
        from: new Date(minStart - 60_000).toISOString(),
        to: new Date(maxEnd + 60_000).toISOString(),
        limit: 500
      })
      logs.value = response.items
    } catch {
      /* keep markers empty on transient errors */
    }
  }

  load()

  return { trace, logs, isLoading, error, notFound, selected }
}
