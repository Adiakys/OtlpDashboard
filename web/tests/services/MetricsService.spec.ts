import { describe, expect, it, vi } from 'vitest'
import { MetricsService } from '~/services/MetricsService'
import type { HttpClientService } from '~/services/HttpClientService'

function stubHttp() {
  return {
    get: vi.fn(async () => ([])),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn()
  } as unknown as HttpClientService & { get: ReturnType<typeof vi.fn> }
}

describe('MetricsService', () => {
  it('calls GET /v1/metrics to list instruments', async () => {
    const http = stubHttp()
    const service = new MetricsService(http)

    await service.listInstruments()

    expect(http.get).toHaveBeenCalledWith('/v1/metrics')
  })

  it('forwards the full instrument key and optional time window to /v1/metrics/points', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce({ instrument: null, points: [] })
    const service = new MetricsService(http)

    await service.getPoints({
      resourceHash: 'abcd',
      scopeName: 'tests',
      instrumentName: 'cpu.load',
      kind: 'Gauge',
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })

    expect(http.get).toHaveBeenCalledWith('/v1/metrics/points', {
      resourceHash: 'abcd',
      scopeName: 'tests',
      instrumentName: 'cpu.load',
      kind: 'Gauge',
      from: '2030-01-01T00:00:00Z',
      to: '2030-01-01T01:00:00Z'
    })
  })

  it('listServices GETs /v1/metrics/services', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce(['svc-1'])
    const service = new MetricsService(http)

    const out = await service.listServices()

    expect(out).toEqual(['svc-1'])
    expect(http.get).toHaveBeenCalledWith('/v1/metrics/services')
  })

  it('omits from/to when not provided (full snapshot)', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce({ instrument: null, points: [] })
    const service = new MetricsService(http)

    await service.getPoints({
      resourceHash: 'abcd',
      scopeName: '',
      instrumentName: 'x',
      kind: 'Sum'
    })

    expect(http.get).toHaveBeenCalledWith('/v1/metrics/points', {
      resourceHash: 'abcd',
      scopeName: '',
      instrumentName: 'x',
      kind: 'Sum',
      from: undefined,
      to: undefined
    })
  })
})
