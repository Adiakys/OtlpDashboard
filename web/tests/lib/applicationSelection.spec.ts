import { describe, expect, it } from 'vitest'
import {
  ALL_APPLICATIONS,
  allState,
  isInstanceChecked,
  normalize,
  onlyInstance,
  onlyService,
  ownerOf,
  parseServicesParam,
  selectedInstanceCount,
  serviceState,
  summarize,
  toggleAll,
  toggleInstance,
  toggleService,
  toList,
  toSelection,
  type ApplicationSelection
} from '~/lib/applicationSelection'
import type { ServiceInstancesDto } from '~/services/types'

const options: ServiceInstancesDto[] = [
  { service: 'api', instances: ['a1', 'a2', 'a3'] },
  { service: 'worker', instances: ['w1'] },
  { service: 'legacy', instances: [] }
]
const api = options[0]!

function sel(services: string[], instances: string[] = []): ApplicationSelection {
  return { services, instances, none: false }
}

describe('applicationSelection', () => {
  it('toggleAll flips between all and none', () => {
    const none = toggleAll(ALL_APPLICATIONS)
    expect(none.none).toBe(true)
    expect(toggleAll(none)).toEqual(ALL_APPLICATIONS)
    expect(toggleAll(sel(['api']))).toEqual(ALL_APPLICATIONS)
  })

  it('allState is partial for any explicit subset', () => {
    expect(allState(ALL_APPLICATIONS)).toBe('checked')
    expect(allState(sel([], ['api:a1']))).toBe('partial')
    expect(allState(toggleAll(ALL_APPLICATIONS))).toBe('unchecked')
  })

  it('deselecting a service from all keeps the others', () => {
    expect(toggleService(ALL_APPLICATIONS, options, 'api')).toEqual(sel(['worker', 'legacy']))
  })

  it('selecting every service collapses back to all', () => {
    expect(toggleService(sel(['worker', 'legacy']), options, 'api')).toEqual(ALL_APPLICATIONS)
  })

  it('deselecting the last service gives none', () => {
    expect(toggleService(sel(['api']), options, 'api').none).toBe(true)
  })

  it('clicking a partial service selects all its instances', () => {
    const next = toggleService(sel(['worker'], ['api:a1']), options, 'api')
    expect(next).toEqual(sel(['worker', 'api']))
  })

  it('unchecking an instance of a selected service keeps its siblings', () => {
    const next = toggleInstance(sel(['api']), options, 'api', 'a2')
    expect(next).toEqual(sel([], ['api:a1', 'api:a3']))
    expect(serviceState(next, api)).toBe('partial')
    expect(selectedInstanceCount(next, api)).toBe(2)
  })

  it('unchecking an instance from all materialises the other services', () => {
    const next = toggleInstance(ALL_APPLICATIONS, options, 'api', 'a1')
    expect(next).toEqual(sel(['worker', 'legacy'], ['api:a2', 'api:a3']))
    expect(isInstanceChecked(next, 'api', 'a1')).toBe(false)
    expect(isInstanceChecked(next, 'worker', 'w1')).toBe(true)
  })

  it('checking the last missing instance collapses to the service', () => {
    const next = toggleInstance(sel(['worker'], ['api:a1', 'api:a3']), options, 'api', 'a2')
    expect(next).toEqual(sel(['worker', 'api']))
  })

  it('unchecking the only selected instance gives none', () => {
    expect(toggleInstance(sel([], ['api:a1']), options, 'api', 'a1').none).toBe(true)
  })

  it('only shortcuts isolate a service or an instance', () => {
    expect(onlyService(options, 'worker')).toEqual(sel(['worker']))
    expect(onlyInstance(options, 'api', 'a2')).toEqual(sel([], ['api:a2']))
    expect(onlyService([api], 'api')).toEqual(ALL_APPLICATIONS)
  })

  it('normalize drops instance keys redundant with their service', () => {
    expect(normalize(sel(['api'], ['api:a1', 'worker:w1']), options)).toEqual(sel(['api', 'worker']))
  })

  it('ownerOf resolves keys through the options, colons included', () => {
    const colons: ServiceInstancesDto[] = [{ service: 'ns:api', instances: ['host:8080', 'host:8081'] }]
    expect(ownerOf('ns:api:host:8080', colons)).toEqual({ service: 'ns:api', instance: 'host:8080' })
    expect(ownerOf('ns:api:gone', colons)).toBeNull()
    expect(toSelection(['ns:api'], false, colons)).toEqual(sel(['ns:api']))
    expect(toSelection(['ns:api:host:8081'], false, colons)).toEqual(sel([], ['ns:api:host:8081']))
    expect(toggleInstance(sel(['ns:api']), colons, 'ns:api', 'host:8080')).toEqual(sel([], ['ns:api:host:8081']))
  })

  it('toSelection and toList round-trip the single services list', () => {
    const selection = toSelection(['worker', 'api:a1'], false, options)
    expect(selection).toEqual(sel(['worker'], ['api:a1']))
    expect(toList(selection)).toEqual(['worker', 'api:a1'])
  })

  it('services URL param keeps instance entries whole', () => {
    expect(parseServicesParam(['a, b', 'api:host:8080', 'api:x,y', 'a'])).toEqual(['a', 'b', 'api:host:8080', 'api:x,y'])
  })

  it('summarize describes the selection for the button label', () => {
    expect(summarize(ALL_APPLICATIONS, options)).toEqual({ kind: 'all' })
    expect(summarize(sel(['api']), options)).toEqual({ kind: 'service', service: 'api' })
    expect(summarize(sel([], ['api:a1']), options)).toEqual({ kind: 'instance', service: 'api', instance: 'a1' })
    expect(summarize(sel([], ['api:a1', 'api:a2']), options)).toEqual({ kind: 'instances', service: 'api', count: 2 })
    expect(summarize(sel(['worker'], ['api:a1']), options)).toEqual({ kind: 'services', count: 2 })
  })
})
