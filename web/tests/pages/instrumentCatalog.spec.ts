import { describe, expect, it, vi } from 'vitest'
import { ref } from 'vue'
import { useInstrumentCatalog, type Resolution } from '~/pages/dashboard/useInstrumentCatalog'
import type { InstrumentDto } from '~/services/types'
import type { MetricsService } from '~/services/MetricsService'

// useState is auto-imported in app code via Nuxt; in unit tests we
// stub it with a plain ref so the catalog state is isolated per case.
declare const globalThis: { useState?: <T>(_k: string, init: () => T) => { value: T } }
globalThis.useState = <T>(_k: string, init: () => T) => ref(init()) as { value: T }
globalThis.computed = <T>(fn: () => T) => ({ value: fn() }) as never

interface FakeMetricsService extends Pick<MetricsService, 'listInstruments'> {}

function makeInstrument(partial: Partial<InstrumentDto>): InstrumentDto {
  return {
    resourceHash: 'h',
    serviceName: null,
    serviceInstanceId: null,
    scopeName: 's',
    name: 'i',
    kind: 'Sum',
    description: null,
    unit: null,
    isMonotonic: false,
    temporality: 'Cumulative',
    pointCount: 1,
    ...partial
  }
}

async function buildCatalog(instruments: InstrumentDto[]) {
  const service: FakeMetricsService = {
    listInstruments: vi.fn().mockResolvedValue(instruments)
  }
  const catalog = useInstrumentCatalog(service as MetricsService)
  await catalog.refresh()
  return catalog
}

describe('useInstrumentCatalog.resolve — service.instance.id semantics', () => {
  const baseBinding = {
    resourceHash: '',
    scopeName: 'System.Runtime',
    instrumentName: 'dotnet.gc.heap',
    kind: 'Sum',
    serviceName: 'sample-server'
  }

  it('returns no-match when the catalog is empty', async () => {
    const catalog = await buildCatalog([])
    const r = catalog.resolve(baseBinding)
    expect(r.kind).toBe('no-match')
  })

  it('returns no-match when nothing matches the logical key', async () => {
    const catalog = await buildCatalog([
      makeInstrument({ scopeName: 'other-scope', name: 'dotnet.gc.heap', kind: 'Sum', serviceName: 'sample-server' })
    ])
    const r = catalog.resolve(baseBinding)
    expect(r.kind).toBe('no-match')
  })

  it('resolves cleanly when exactly one instrument matches the logical key (single instance)', async () => {
    const catalog = await buildCatalog([
      makeInstrument({
        resourceHash: 'h-only',
        scopeName: 'System.Runtime', name: 'dotnet.gc.heap', kind: 'Sum',
        serviceName: 'sample-server', serviceInstanceId: 'server-1'
      })
    ])
    const r = catalog.resolve(baseBinding) as Extract<Resolution, { kind: 'resolved' }>
    expect(r.kind).toBe('resolved')
    expect(r.binding.resourceHash).toBe('h-only')
    expect(r.binding.serviceInstanceId).toBe('server-1')
  })

  it('returns ambiguous (no pin) when multiple instruments match', async () => {
    const catalog = await buildCatalog([
      makeInstrument({
        resourceHash: 'h-1', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'server-1'
      }),
      makeInstrument({
        resourceHash: 'h-2', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'server-2'
      })
    ])
    const r = catalog.resolve(baseBinding) as Extract<Resolution, { kind: 'ambiguous' }>
    expect(r.kind).toBe('ambiguous')
    expect(r.requestedId).toBeNull()
    expect(r.available).toEqual(['server-1', 'server-2'])
  })

  it('resolves to the pinned instance when a serviceInstanceId is configured and matches', async () => {
    const catalog = await buildCatalog([
      makeInstrument({
        resourceHash: 'h-1', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'server-1'
      }),
      makeInstrument({
        resourceHash: 'h-2', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'server-2'
      })
    ])
    const r = catalog.resolve({ ...baseBinding, serviceInstanceId: 'server-2' }) as Extract<Resolution, { kind: 'resolved' }>
    expect(r.kind).toBe('resolved')
    expect(r.binding.resourceHash).toBe('h-2')
    expect(r.binding.serviceInstanceId).toBe('server-2')
  })

  it('returns ambiguous (with requestedId) when the pin is missing from the catalog', async () => {
    const catalog = await buildCatalog([
      makeInstrument({
        resourceHash: 'h-1', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'server-1'
      })
    ])
    const r = catalog.resolve({ ...baseBinding, serviceInstanceId: 'server-99' }) as Extract<Resolution, { kind: 'ambiguous' }>
    expect(r.kind).toBe('ambiguous')
    expect(r.requestedId).toBe('server-99')
    expect(r.available).toEqual(['server-1'])
  })

  it('matches service-agnostically when the binding has no serviceName (older exports)', async () => {
    const catalog = await buildCatalog([
      makeInstrument({
        resourceHash: 'h-any', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'server-1'
      })
    ])
    const r = catalog.resolve({ ...baseBinding, serviceName: null }) as Extract<Resolution, { kind: 'resolved' }>
    expect(r.kind).toBe('resolved')
    expect(r.binding.resourceHash).toBe('h-any')
  })

  it('deduplicates and sorts the available list deterministically', async () => {
    const catalog = await buildCatalog([
      makeInstrument({
        resourceHash: 'h-c', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'gamma'
      }),
      makeInstrument({
        resourceHash: 'h-a', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'alpha'
      }),
      makeInstrument({
        resourceHash: 'h-b', scopeName: 'System.Runtime', name: 'dotnet.gc.heap',
        kind: 'Sum', serviceName: 'sample-server', serviceInstanceId: 'beta'
      })
    ])
    const r = catalog.resolve(baseBinding) as Extract<Resolution, { kind: 'ambiguous' }>
    expect(r.kind).toBe('ambiguous')
    expect(r.available).toEqual(['alpha', 'beta', 'gamma'])
  })
})
