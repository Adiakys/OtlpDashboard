import { describe, expect, it, vi } from 'vitest'
import { InfoService } from '~/services/InfoService'
import type { HttpClientService } from '~/services/HttpClientService'

function stubHttp() {
  return {
    get: vi.fn(async () => ({ applicationName: 'Something', version: '0.0.0' })),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn()
  } as unknown as HttpClientService & { get: ReturnType<typeof vi.fn> }
}

describe('InfoService', () => {
  it('calls GET /v1/info', async () => {
    const http = stubHttp()
    const service = new InfoService(http)

    await service.getInfo()

    expect(http.get).toHaveBeenCalledWith('/v1/info')
  })

  it('returns the DashboardInfoDto payload with applicationName and version', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce({ applicationName: 'Acme', version: '1.2.3' })
    const service = new InfoService(http)

    const result = await service.getInfo()

    expect(result).toEqual({ applicationName: 'Acme', version: '1.2.3' })
  })

  it('accepts a null version (unauthenticated response)', async () => {
    const http = stubHttp()
    http.get.mockResolvedValueOnce({ applicationName: 'Acme', version: null })
    const service = new InfoService(http)

    const result = await service.getInfo()

    expect(result.version).toBeNull()
  })
})
