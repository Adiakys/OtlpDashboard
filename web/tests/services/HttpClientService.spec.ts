import { describe, expect, it, vi } from 'vitest'
import type { $Fetch } from 'ofetch'
import { HttpClientService } from '~/services/HttpClientService'

function createFetchStub(): $Fetch & { mock: ReturnType<typeof vi.fn> } {
  const mock = vi.fn(async () => ({}))
  return mock as unknown as $Fetch & { mock: ReturnType<typeof vi.fn> }
}

describe('HttpClientService', () => {
  it('sends GET with baseURL and query', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', () => null, fetcher)

    await http.get('/v1/logs', { from: 'x', to: 'y' })

    expect(fetcher).toHaveBeenCalledWith('/v1/logs', {
      baseURL: '/api',
      method: 'GET',
      query: { from: 'x', to: 'y' },
      headers: {}
    })
  })

  it('sends POST with body', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', () => null, fetcher)

    await http.post('/v1/things', { id: 42 })

    expect(fetcher).toHaveBeenCalledWith('/v1/things', {
      baseURL: '/api',
      method: 'POST',
      body: { id: 42 },
      headers: {}
    })
  })

  it('sends PUT and DELETE', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', () => null, fetcher)

    await http.put('/v1/x', { a: 1 })
    await http.delete('/v1/x', { id: 9 })

    expect(fetcher).toHaveBeenNthCalledWith(1, '/v1/x', {
      baseURL: '/api',
      method: 'PUT',
      body: { a: 1 },
      headers: {}
    })
    expect(fetcher).toHaveBeenNthCalledWith(2, '/v1/x', {
      baseURL: '/api',
      method: 'DELETE',
      query: { id: 9 },
      headers: {}
    })
  })

  it('returns the fetcher result typed as T', async () => {
    const fetcher = vi.fn(async () => ({ items: [], nextCursor: null })) as unknown as $Fetch
    const http = new HttpClientService('/api', () => null, fetcher)

    const result = await http.get<{ items: unknown[], nextCursor: string | null }>('/v1/logs')

    expect(result).toEqual({ items: [], nextCursor: null })
  })

  it('adds Authorization: Bearer <token> when the provider returns a value', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', () => 'secret-xyz', fetcher)

    await http.get('/v1/logs')

    expect(fetcher).toHaveBeenCalledWith('/v1/logs', {
      baseURL: '/api',
      method: 'GET',
      query: undefined,
      headers: { Authorization: 'Bearer secret-xyz' }
    })
  })

  it('omits Authorization header when the provider returns null', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', () => null, fetcher)

    await http.get('/v1/logs')

    const call = fetcher.mock.calls[0]![1] as { headers: Record<string, string> }
    expect(call.headers).toEqual({})
  })

  it('re-reads the token on each request', async () => {
    const fetcher = createFetchStub()
    let current: string | null = 'first'
    const http = new HttpClientService('/api', () => current, fetcher)

    await http.get('/v1/a')
    current = 'second'
    await http.get('/v1/b')
    current = null
    await http.get('/v1/c')

    expect((fetcher.mock.calls[0]![1] as { headers: Record<string, string> }).headers)
      .toEqual({ Authorization: 'Bearer first' })
    expect((fetcher.mock.calls[1]![1] as { headers: Record<string, string> }).headers)
      .toEqual({ Authorization: 'Bearer second' })
    expect((fetcher.mock.calls[2]![1] as { headers: Record<string, string> }).headers)
      .toEqual({})
  })
})
