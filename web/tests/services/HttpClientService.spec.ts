import { describe, expect, it, vi } from 'vitest'
import type { $Fetch } from 'ofetch'
import { HttpClientService } from '~/services/HttpClientService'

function createFetchStub(): $Fetch & { mock: ReturnType<typeof vi.fn> } {
  const mock = vi.fn(async () => ({}))
  return mock as unknown as $Fetch & { mock: ReturnType<typeof vi.fn> }
}

describe('HttpClientService', () => {
  it('sends GET with baseURL, query, credentials and a timeout signal', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.get('/v1/logs', { from: 'x', to: 'y' })

    const [path, opts] = fetcher.mock.calls[0]! as [string, Record<string, unknown>]
    expect(path).toBe('/v1/logs')
    expect(opts.baseURL).toBe('/api')
    expect(opts.method).toBe('GET')
    expect(opts.query).toEqual({ from: 'x', to: 'y' })
    expect(opts.credentials).toBe('include')
    // The deadline is folded into the signal (ofetch ignores `timeout`
    // whenever a signal is present), so no bare `timeout` is forwarded.
    expect(opts.timeout).toBeUndefined()
    expect(opts.signal).toBeInstanceOf(AbortSignal)
    expect((opts.signal as AbortSignal).aborted).toBe(false)
  })

  it('arms the timeout even with no caller signal: the signal aborts on deadline', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher, 10)

    await http.get('/v1/logs')

    const opts = fetcher.mock.calls[0]![1] as { signal?: AbortSignal }
    expect(opts.signal).toBeInstanceOf(AbortSignal)
    await new Promise(resolve => setTimeout(resolve, 30))
    expect(opts.signal!.aborted).toBe(true)
    expect(opts.signal!.reason?.name).toBe('TimeoutError')
  })

  it('aborts when the caller signal aborts (timeout folded in)', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)
    const controller = new AbortController()

    await http.get('/v1/logs', undefined, { signal: controller.signal })

    const opts = fetcher.mock.calls[0]![1] as { signal?: AbortSignal }
    expect(opts.signal).toBeInstanceOf(AbortSignal)
    expect(opts.signal!.aborted).toBe(false)
    controller.abort()
    expect(opts.signal!.aborted).toBe(true)
  })

  it('disables the timeout when configured to 0', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher, 0)

    await http.get('/v1/logs')

    const opts = fetcher.mock.calls[0]![1] as { signal?: AbortSignal }
    expect(opts.signal).toBeUndefined()
  })

  it('sends POST with body and credentials', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.post('/v1/things', { id: 42 })

    const opts = fetcher.mock.calls[0]![1] as Record<string, unknown>
    expect(opts.method).toBe('POST')
    expect(opts.body).toEqual({ id: 42 })
    expect(opts.credentials).toBe('include')
    expect(opts.signal).toBeInstanceOf(AbortSignal)
  })

  it('sends PUT and DELETE with credentials', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.put('/v1/x', { a: 1 })
    await http.delete('/v1/x', { id: 9 })

    const put = fetcher.mock.calls[0]![1] as Record<string, unknown>
    expect(put.method).toBe('PUT')
    expect(put.body).toEqual({ a: 1 })
    expect(put.credentials).toBe('include')

    const del = fetcher.mock.calls[1]![1] as Record<string, unknown>
    expect(del.method).toBe('DELETE')
    expect(del.query).toEqual({ id: 9 })
    expect(del.credentials).toBe('include')
  })

  it('returns the fetcher result typed as T', async () => {
    const fetcher = vi.fn(async () => ({ items: [], nextCursor: null })) as unknown as $Fetch
    const http = new HttpClientService('/api', fetcher)

    const result = await http.get<{ items: unknown[], nextCursor: string | null }>('/v1/logs')

    expect(result).toEqual({ items: [], nextCursor: null })
  })

  it('does not attach Authorization headers (auth travels via cookie)', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.get('/v1/logs')

    const opts = fetcher.mock.calls[0]![1] as { headers?: Record<string, string> }
    expect(opts.headers).toBeUndefined()
  })
})
