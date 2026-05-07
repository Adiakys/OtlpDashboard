import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useLogsPage } from '~/pages/logs/usePage'
import type { HttpClientService } from '~/services/HttpClientService'
import { LogsService } from '~/services/LogsService'
import type { LogRecordDto, PagedResponse } from '~/services/types'

function installDocumentStub() {
  vi.stubGlobal('document', {
    visibilityState: 'visible',
    addEventListener: () => {},
    removeEventListener: () => {}
  })
}

function makeLog(overrides: Partial<LogRecordDto> = {}): LogRecordDto {
  return {
    time: overrides.time ?? '2030-01-01T00:00:00.000Z',
    observedTime: null,
    severityNumber: 9,
    severityText: 'INFO',
    body: overrides.body ?? 'hello',
    traceId: null,
    spanId: overrides.spanId ?? null,
    scopeName: null,
    scopeVersion: null,
    resourceHash: 'abcd',
    attributes: {}
  }
}

function stubHttp(pages: Array<PagedResponse<LogRecordDto>>) {
  const logsPages = [...pages]
  // The page composable fires a parallel /v1/logs/services call on mount and
  // on window change. We stub it with an empty list; only /v1/logs responses
  // feed the queued pages.
  const get = vi.fn(async (path: string) => {
    if (path === '/v1/logs/services') return [] as string[]
    return logsPages.shift() ?? { items: [], nextCursor: null }
  })
  return {
    client: {
      get,
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn()
    } as unknown as HttpClientService,
    get
  }
}

describe('useLogsPage — live mode', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    installDocumentStub()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('prepends new log records on tick and de-duplicates overlap', async () => {
    // Initial reload returns one record; first live tick returns a mix of
    // the existing record (duplicate) + one new; second tick returns one new.
    const initial = makeLog({ time: '2030-01-01T00:00:10.000Z', spanId: 'aaaaaaaaaaaaaaaa' })
    const tick1Existing = initial // same key — must be deduped
    const tick1Fresh = makeLog({ time: '2030-01-01T00:00:20.000Z', spanId: 'bbbbbbbbbbbbbbbb' })
    const tick2Fresh = makeLog({ time: '2030-01-01T00:00:30.000Z', spanId: 'cccccccccccccccc' })

    const http = stubHttp([
      { items: [initial], nextCursor: null },
      { items: [tick1Fresh, tick1Existing], nextCursor: null }, // DESC order
      { items: [tick2Fresh], nextCursor: null }
    ])
    const service = new LogsService(http.client)

    const page = useLogsPage(service, { autoLive: false })
    await vi.advanceTimersByTimeAsync(0) // flush initial reload

    expect(page.items.value).toHaveLength(1)
    expect(page.items.value[0]?.spanId).toBe('aaaaaaaaaaaaaaaa')

    page.toggleLive()
    await vi.advanceTimersByTimeAsync(0) // immediate first live tick

    // Prepended: [tick1Fresh, initial] — dedup drops the overlap.
    expect(page.items.value).toHaveLength(2)
    expect(page.items.value[0]?.spanId).toBe('bbbbbbbbbbbbbbbb')
    expect(page.items.value[1]?.spanId).toBe('aaaaaaaaaaaaaaaa')

    await vi.advanceTimersByTimeAsync(5000) // next interval tick
    expect(page.items.value).toHaveLength(3)
    expect(page.items.value[0]?.spanId).toBe('cccccccccccccccc')

    page.toggleLive()
  })

  it('reloads with the new severity filter when toggled in live mode', async () => {
    // Initial unfiltered fetch returns one Info log; after the user
    // toggles a severity bucket the watcher must call /v1/logs again
    // with the new `severities` param even though live mode is on,
    // and the items list must replace with the filtered response.
    const initial = makeLog({ time: '2030-01-01T00:00:10.000Z', spanId: 'aaaaaaaaaaaaaaaa' })
    const filteredOnly = makeLog({
      time: '2030-01-01T00:00:15.000Z',
      spanId: 'eeeeeeeeeeeeeeee',
      severityText: 'WARN'
    })
    filteredOnly.severityNumber = 13
    const http = stubHttp([
      // 1: initial reload
      { items: [initial], nextCursor: null },
      // 2: immediate first live tick after toggleLive — empty so no
      //    new rows interfere with the test's assertion.
      { items: [], nextCursor: null },
      // 3: forced reload from the severity change (the actual subject
      //    of this test).
      { items: [filteredOnly], nextCursor: null }
    ])
    const service = new LogsService(http.client)

    const page = useLogsPage(service, { autoLive: false })
    await vi.advanceTimersByTimeAsync(0)
    expect(page.items.value).toHaveLength(1)

    // Enter live mode and let the immediate first tick drain.
    page.toggleLive()
    await vi.advanceTimersByTimeAsync(0)
    // Then change the severity filter — the watcher must reload
    // immediately even though live mode is on.
    page.severityFilter.value = ['warn']
    await vi.advanceTimersByTimeAsync(0)

    // The single item now visible is the WARN-only response of the
    // forced reload — proves the filter went out and the items list
    // was wiped + repopulated.
    expect(page.items.value).toHaveLength(1)
    expect(page.items.value[0]?.spanId).toBe('eeeeeeeeeeeeeeee')

    const logsCalls = http.get.mock.calls.filter(c => c[0] === '/v1/logs')
    expect(logsCalls.at(-1)?.[1]).toMatchObject({ severities: 'warn' })

    page.toggleLive()
  })

  it('forwards Bearer-less fetch with traceId filter during live ticks', async () => {
    const http = stubHttp([
      { items: [], nextCursor: null },
      { items: [], nextCursor: null }
    ])
    const service = new LogsService(http.client)

    const page = useLogsPage(service, {
      initialTraceId: 'deadbeefdeadbeefdeadbeefdeadbeef',
      autoLive: false
    })
    await vi.advanceTimersByTimeAsync(0)

    page.toggleLive()
    await vi.advanceTimersByTimeAsync(0)

    // Filter the /v1/logs requests (ignoring the parallel /services lookup)
    // and inspect the delta-fetch call issued by the first live tick.
    const logsCalls = http.get.mock.calls.filter(c => c[0] === '/v1/logs')
    expect(logsCalls.length).toBeGreaterThanOrEqual(2)
    const liveCall = logsCalls[1]
    expect(liveCall?.[1]).toMatchObject({
      traceId: 'deadbeefdeadbeefdeadbeefdeadbeef',
      limit: 500,
      cursor: undefined
    })

    page.toggleLive()
  })
})
