import { describe, expect, it, vi } from 'vitest'
import type { $Fetch } from 'ofetch'
import { HttpClientService } from '~/services/HttpClientService'

function createFetchStub(): $Fetch & { mock: ReturnType<typeof vi.fn> } {
  const mock = vi.fn(async () => ({}))
  return mock as unknown as $Fetch & { mock: ReturnType<typeof vi.fn> }
}

describe('HttpClientService', () => {
  it('sends GET with baseURL and query, including credentials', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.get('/v1/logs', { from: 'x', to: 'y' })

    expect(fetcher).toHaveBeenCalledWith('/v1/logs', {
      baseURL: '/api',
      method: 'GET',
      query: { from: 'x', to: 'y' },
      credentials: 'include'
    })
  })

  it('sends POST with body and credentials', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.post('/v1/things', { id: 42 })

    expect(fetcher).toHaveBeenCalledWith('/v1/things', {
      baseURL: '/api',
      method: 'POST',
      body: { id: 42 },
      credentials: 'include'
    })
  })

  it('sends PUT and DELETE with credentials', async () => {
    const fetcher = createFetchStub()
    const http = new HttpClientService('/api', fetcher)

    await http.put('/v1/x', { a: 1 })
    await http.delete('/v1/x', { id: 9 })

    expect(fetcher).toHaveBeenNthCalledWith(1, '/v1/x', {
      baseURL: '/api',
      method: 'PUT',
      body: { a: 1 },
      credentials: 'include'
    })
    expect(fetcher).toHaveBeenNthCalledWith(2, '/v1/x', {
      baseURL: '/api',
      method: 'DELETE',
      query: { id: 9 },
      credentials: 'include'
    })
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
