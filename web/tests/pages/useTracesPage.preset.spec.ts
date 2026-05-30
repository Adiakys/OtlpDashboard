import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useTracesPage } from '~/pages/traces/usePage'
import type { HttpClientService } from '~/services/HttpClientService'
import { TraceService } from '~/services/TraceService'
import type { PagedResponse, TraceSummaryDto } from '~/services/types'

function installDocumentStub() {
  vi.stubGlobal('document', {
    visibilityState: 'visible',
    addEventListener: () => {},
    removeEventListener: () => {}
  })
}

function stubHttp(pages: Array<PagedResponse<TraceSummaryDto>>) {
  const tracesPages = [...pages]
  const get = vi.fn(async (path: string) => {
    if (path === '/v1/traces/services') return [] as string[]
    return tracesPages.shift() ?? { items: [], nextCursor: null }
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

describe('useTracesPage — rolling preset hydration', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    installDocumentStub()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('recomputes window from now when a known preset is supplied, ignoring stale initialRange', async () => {
    // Pin the clock so the expected window is deterministic.
    const fixedNow = new Date('2030-06-01T12:00:00.000Z')
    vi.setSystemTime(fixedNow)

    const http = stubHttp([{ items: [], nextCursor: null }])
    const service = new TraceService(http.client)

    // The "stale" absolute range mimics what the URL still carries
    // from a router.replace that fired the last time the user was on
    // the page — older than now, and definitely not what they want
    // when they hit Back from a trace detail.
    const stale = {
      from: '2030-06-01T08:00:00.000Z',
      to: '2030-06-01T09:00:00.000Z'
    }

    const page = useTracesPage(service, {
      initialRange: stale,
      initialPreset: '1h',
      autoLive: false
    })
    await vi.advanceTimersByTimeAsync(0)

    // Window is recomputed: to=now, from=now-1h. The stale URL
    // timestamps are ignored.
    expect(page.range.value.to).toBe('2030-06-01T12:00:00.000Z')
    expect(page.range.value.from).toBe('2030-06-01T11:00:00.000Z')
    expect(page.rangePreset.value).toBe('1h')

    // queryState round-trips as `?range=1h` (no from/to leaks).
    expect(page.queryState.value).toMatchObject({ range: '1h' })
    expect(page.queryState.value.from).toBeUndefined()
    expect(page.queryState.value.to).toBeUndefined()

    // The initial /v1/traces fetch used the recomputed window, not
    // the stale URL timestamps.
    const tracesCalls = http.get.mock.calls.filter(c => c[0] === '/v1/traces')
    expect(tracesCalls[0]?.[1]).toMatchObject({
      from: '2030-06-01T11:00:00.000Z',
      to: '2030-06-01T12:00:00.000Z'
    })
  })

  it('defaults to the rolling 1h preset when neither preset nor range is in the URL', async () => {
    vi.setSystemTime(new Date('2030-06-01T12:00:00.000Z'))
    const http = stubHttp([{ items: [], nextCursor: null }])
    const service = new TraceService(http.client)

    const page = useTracesPage(service, { autoLive: false })
    await vi.advanceTimersByTimeAsync(0)

    // Default is the rolling preset, not an absolute window — otherwise
    // a fresh visit's URL would freeze the timestamps and back-nav from
    // a trace detail would land on a stale window.
    expect(page.rangePreset.value).toBe('1h')
    expect(page.queryState.value).toMatchObject({ range: '1h' })
    expect(page.queryState.value.from).toBeUndefined()
    expect(page.queryState.value.to).toBeUndefined()
  })

  it('falls back to initialRange when the preset is unknown', async () => {
    vi.setSystemTime(new Date('2030-06-01T12:00:00.000Z'))
    const http = stubHttp([{ items: [], nextCursor: null }])
    const service = new TraceService(http.client)

    const explicit = {
      from: '2030-06-01T08:00:00.000Z',
      to: '2030-06-01T09:00:00.000Z'
    }
    const page = useTracesPage(service, {
      initialRange: explicit,
      initialPreset: 'not-a-preset',
      autoLive: false
    })
    await vi.advanceTimersByTimeAsync(0)

    expect(page.rangePreset.value).toBeNull()
    expect(page.range.value).toEqual(explicit)
    expect(page.queryState.value).toMatchObject({
      from: explicit.from,
      to: explicit.to
    })
    expect(page.queryState.value.range).toBeUndefined()
  })
})
