import { describe, expect, it, vi } from 'vitest'
import { WidgetService } from '~/services/WidgetService'
import type { HttpClientService } from '~/services/HttpClientService'

function stubHttp() {
  return {
    get: vi.fn(async () => ([])),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn()
  } as unknown as HttpClientService & {
    get: ReturnType<typeof vi.fn>
    post: ReturnType<typeof vi.fn>
    put: ReturnType<typeof vi.fn>
    delete: ReturnType<typeof vi.fn>
  }
}

describe('WidgetService', () => {
  it('lists custom definitions via GET /v1/widgets/definitions', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.listCustom()

    expect(http.get).toHaveBeenCalledWith('/v1/widgets/definitions')
  })

  it('fetches one custom definition by id, encoded', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.getCustom('a/b c')

    expect(http.get).toHaveBeenCalledWith('/v1/widgets/definitions/a%2Fb%20c')
  })

  it('creates a definition via POST', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    const req = {
      name: 'p99',
      description: null,
      icon: 'i-ph-target',
      engine: 'Preset' as const,
      baseKind: 'metric-stat',
      config: { calc: 'last' },
      spec: null,
      defaultW: 4,
      defaultH: 3,
      rowVersion: 0
    }
    await service.createCustom(req)

    expect(http.post).toHaveBeenCalledWith('/v1/widgets/definitions', req)
  })

  it('updates a definition via PUT, id encoded', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    const req = {
      name: 'p99-v2',
      description: 'edited',
      icon: 'i-ph-target',
      engine: 'Preset' as const,
      baseKind: 'metric-stat',
      config: {},
      spec: null,
      defaultW: 3,
      defaultH: 3,
      rowVersion: 1
    }
    await service.updateCustom('id with space', req)

    expect(http.put).toHaveBeenCalledWith('/v1/widgets/definitions/id%20with%20space', req)
  })

  it('deletes a definition via DELETE with rowVersion query param', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.deleteCustom('abc', 7)

    expect(http.delete).toHaveBeenCalledWith(
      '/v1/widgets/definitions/abc',
      { rowVersion: 7 }
    )
  })

  it('lists libraries via GET /v1/widgets/libraries', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.listLibraries()

    expect(http.get).toHaveBeenCalledWith('/v1/widgets/libraries')
  })

  it('reloads packs via POST /v1/packs/reload', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.reloadPacks()

    expect(http.post).toHaveBeenCalledWith('/v1/packs/reload')
  })

  it('installs a pack via POST /v1/packs/install (no path)', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.installPack({ url: 'https://github.com/org/pack', ref: 'v1.2.0' })

    expect(http.post).toHaveBeenCalledWith('/v1/packs/install',
      { url: 'https://github.com/org/pack', ref: 'v1.2.0' })
  })

  it('installs a pack via POST /v1/packs/install (with sub-path)', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.installPack({
      url: 'https://github.com/org/monorepo',
      ref: 'v1.2.0',
      path: 'packs/team'
    })

    expect(http.post).toHaveBeenCalledWith('/v1/packs/install',
      { url: 'https://github.com/org/monorepo', ref: 'v1.2.0', path: 'packs/team' })
  })

  it('updates a pack via POST .../update, id encoded', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.updatePack('team-pack')

    expect(http.post).toHaveBeenCalledWith('/v1/packs/team-pack/update')
  })

  it('uninstalls a pack via DELETE, id encoded', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.uninstallPack('team pack')

    expect(http.delete).toHaveBeenCalledWith('/v1/packs/team%20pack')
  })

  it('lists packs via GET /v1/packs', async () => {
    const http = stubHttp()
    const service = new WidgetService(http)

    await service.listPacks()

    expect(http.get).toHaveBeenCalledWith('/v1/packs')
  })
})
