import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useTracesPage } from '~/pages/traces/usePage'
import type { HttpClientService, RequestOptions } from '~/services/HttpClientService'
import { TraceService } from '~/services/TraceService'
import type { PagedResponse, TraceSummaryDto } from '~/services/types'

function installDocumentStub() {
  vi.stubGlobal('document', {
    visibilityState: 'visible',
    addEventListener: () => {},
    removeEventListener: () => {}
  })
}

const EMPTY: PagedResponse<TraceSummaryDto> = { items: [], nextCursor: null }

describe('useTracesPage — in-flight request lifecycle', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    installDocumentStub()
  })
  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('aborts the previous list request when a filter change supersedes it', async () => {
    const signals: AbortSignal[] = []
    // /v1/traces calls hang until manually resolved so they stay in-flight;
    // /v1/traces/services resolves immediately.
    const get = vi.fn((path: string, _query?: unknown, options?: RequestOptions) => {
      if (path === '/v1/traces/services') return Promise.resolve([] as string[])
      if (options?.signal) signals.push(options.signal)
      return new Promise<PagedResponse<TraceSummaryDto>>(() => {})
    })
    const client = { get, post: vi.fn(), put: vi.fn(), delete: vi.fn() } as unknown as HttpClientService
    const service = new TraceService(client)

    const page = useTracesPage(service, { autoLive: false })
    await vi.advanceTimersByTimeAsync(0)

    // The initial fetch is still pending; changing the filter starts a new
    // one that must abort the first.
    page.service.value = ['a']
    await vi.advanceTimersByTimeAsync(0)

    expect(signals.length).toBe(2)
    expect(signals[0]!.aborted).toBe(true)
    expect(signals[1]!.aborted).toBe(false)
  })

  it('does not surface an error when the in-flight request is aborted', async () => {
    let rejectFirst: ((reason: unknown) => void) | null = null
    let call = 0
    const get = vi.fn((path: string) => {
      if (path === '/v1/traces/services') return Promise.resolve([] as string[])
      call += 1
      if (call === 1) {
        // Mimic ofetch rejecting an aborted request after the supersede.
        return new Promise<PagedResponse<TraceSummaryDto>>((_, reject) => { rejectFirst = reject })
      }
      return Promise.resolve(EMPTY)
    })
    const client = { get, post: vi.fn(), put: vi.fn(), delete: vi.fn() } as unknown as HttpClientService
    const service = new TraceService(client)

    const page = useTracesPage(service, { autoLive: false })
    await vi.advanceTimersByTimeAsync(0)

    page.service.value = ['a']
    await vi.advanceTimersByTimeAsync(0)

    // The first request rejects after being aborted — must be swallowed.
    rejectFirst?.(new Error('aborted'))
    await vi.advanceTimersByTimeAsync(0)

    expect(page.error.value).toBeNull()
    expect(page.isLoading.value).toBe(false)
  })
})
