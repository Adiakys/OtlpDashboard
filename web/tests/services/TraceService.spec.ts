import { describe, expect, it, vi } from 'vitest'
import { TraceService } from '~/services/TraceService'
import type { HttpClientService } from '~/services/HttpClientService'

function stubHttp() {
  return {
    get: vi.fn(async () => ({ items: [], nextCursor: null })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn()
  } as unknown as HttpClientService & { get: ReturnType<typeof vi.fn> }
}

describe('TraceService', () => {
  it('listTraces calls GET /v1/traces with the window + pagination params', async () => {
    const http = stubHttp()
    const service = new TraceService(http)

    await service.listTraces({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: 25
    })

    expect(http.get).toHaveBeenCalledWith('/v1/traces', expect.objectContaining({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: 25,
      cursor: undefined,
      services: undefined
    }), undefined)
  })

  it('listTraces forwards the services allow-list as CSV', async () => {
    const http = stubHttp()
    const service = new TraceService(http)

    await service.listTraces({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      services: ['api', 'auth']
    })

    expect(http.get).toHaveBeenCalledWith('/v1/traces', expect.objectContaining({
      services: 'api,auth'
    }), undefined)
  })

  it('getTrace calls GET /v1/traces/{id}', async () => {
    const http = stubHttp()
    const service = new TraceService(http)

    await service.getTrace('deadbeefdeadbeefdeadbeefdeadbeef')

    expect(http.get).toHaveBeenCalledWith('/v1/traces/deadbeefdeadbeefdeadbeefdeadbeef')
  })

  it('listServices GETs /v1/traces/services with the window', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce(['one', 'two'])
    const service = new TraceService(http)

    const out = await service.listServices({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })

    expect(out).toEqual(['one', 'two'])
    expect(http.get).toHaveBeenCalledWith('/v1/traces/services', {
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })
  })
})
