import { describe, expect, it, vi } from 'vitest'
import { LogsService } from '~/services/LogsService'
import type { HttpClientService } from '~/services/HttpClientService'

function stubHttp() {
  return {
    get: vi.fn(async () => ({ items: [], nextCursor: null })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn()
  } as unknown as HttpClientService & { get: ReturnType<typeof vi.fn> }
}

describe('LogsService', () => {
  it('calls GET /v1/logs with the window + pagination params', async () => {
    const http = stubHttp()
    const service = new LogsService(http)

    await service.listLogs({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: 50,
      cursor: 'abc'
    })

    expect(http.get).toHaveBeenCalledWith('/v1/logs', {
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: 50,
      cursor: 'abc',
      traceId: undefined,
      services: undefined
    })
  })

  it('forwards undefined cursor/limit/traceId/service when absent', async () => {
    const http = stubHttp()
    const service = new LogsService(http)

    await service.listLogs({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })

    expect(http.get).toHaveBeenCalledWith('/v1/logs', {
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: undefined,
      cursor: undefined,
      traceId: undefined,
      services: undefined
    })
  })

  it('forwards traceId filter when set', async () => {
    const http = stubHttp()
    const service = new LogsService(http)

    await service.listLogs({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      traceId: 'deadbeefdeadbeefdeadbeefdeadbeef'
    })

    expect(http.get).toHaveBeenCalledWith('/v1/logs', {
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: undefined,
      cursor: undefined,
      traceId: 'deadbeefdeadbeefdeadbeefdeadbeef',
      service: undefined
    })
  })

  it('forwards services allow-list joined as CSV', async () => {
    const http = stubHttp()
    const service = new LogsService(http)

    await service.listLogs({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      services: ['frontend', 'auth']
    })

    expect(http.get).toHaveBeenCalledWith('/v1/logs', {
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      limit: undefined,
      cursor: undefined,
      traceId: undefined,
      services: 'frontend,auth'
    })
  })

  it('omits services when the allow-list is empty', async () => {
    const http = stubHttp()
    const service = new LogsService(http)

    await service.listLogs({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z',
      services: []
    })

    expect(http.get).toHaveBeenCalledWith('/v1/logs', expect.objectContaining({
      services: undefined
    }))
  })

  it('listServices GETs /v1/logs/services with the window', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce(['a', 'b'])
    const service = new LogsService(http)

    const out = await service.listServices({
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })

    expect(out).toEqual(['a', 'b'])
    expect(http.get).toHaveBeenCalledWith('/v1/logs/services', {
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })
  })
})
