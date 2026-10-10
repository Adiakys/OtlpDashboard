import type { ServiceInstancesDto } from '~/services/types'

/**
 * Application picker state. Empty `services` and `instances` with `none`
 * false is the implicit "all" form; `none` is the explicit "deselect all".
 * A service listed in `services` includes every instance, current or future;
 * `instances` holds `service:instanceId` keys for partially selected services.
 * Pages and the API carry both as one `services` list (see `toSelection`);
 * keys are never split on the colon, since either part may contain one.
 */
export interface ApplicationSelection {
  services: string[]
  instances: string[]
  none: boolean
}

export type CheckState = 'checked' | 'partial' | 'unchecked'

export const ALL_APPLICATIONS: ApplicationSelection = { services: [], instances: [], none: false }
const NO_APPLICATIONS: ApplicationSelection = { services: [], instances: [], none: true }

export function instanceKey(service: string, instance: string): string {
  return `${service}:${instance}`
}

/** Service and instance behind a key, looked up in the options; null for
 *  keys of instances no longer listed. */
export function ownerOf(key: string, options: ServiceInstancesDto[]): { service: string, instance: string } | null {
  for (const option of options) {
    const instance = option.instances.find(i => instanceKey(option.service, i) === key)
    if (instance !== undefined) return { service: option.service, instance }
  }
  return null
}

/** Splits the single `services` list into services and known instance keys. */
export function toSelection(list: string[], none: boolean, options: ServiceInstancesDto[]): ApplicationSelection {
  const isInstance = (entry: string) => ownerOf(entry, options) !== null
  return {
    services: list.filter(e => !isInstance(e)),
    instances: list.filter(isInstance),
    none
  }
}

export function toList(sel: ApplicationSelection): string[] {
  return [...sel.services, ...sel.instances]
}

/** Reads `services=` entries: plain ones may be CSV (older links), `name:instance` ones are kept whole. */
export function parseServicesParam(entries: string[]): string[] {
  const out: string[] = []
  for (const entry of entries) {
    const parts = entry.includes(':') ? [entry.trim()] : entry.split(',').map(p => p.trim())
    for (const part of parts) {
      if (part.length > 0 && !out.includes(part)) out.push(part)
    }
  }
  return out
}

export function isAll(sel: ApplicationSelection): boolean {
  return !sel.none && sel.services.length === 0 && sel.instances.length === 0
}

export function allState(sel: ApplicationSelection): CheckState {
  if (sel.none) return 'unchecked'
  return isAll(sel) ? 'checked' : 'partial'
}

export function selectedInstanceCount(sel: ApplicationSelection, option: ServiceInstancesDto): number {
  if (sel.none) return 0
  if (isAll(sel) || sel.services.includes(option.service)) return option.instances.length
  return option.instances.filter(i => sel.instances.includes(instanceKey(option.service, i))).length
}

export function serviceState(sel: ApplicationSelection, option: ServiceInstancesDto): CheckState {
  if (sel.none) return 'unchecked'
  if (isAll(sel) || sel.services.includes(option.service)) return 'checked'
  const count = selectedInstanceCount(sel, option)
  if (count === 0) return 'unchecked'
  return count === option.instances.length ? 'checked' : 'partial'
}

export function isInstanceChecked(sel: ApplicationSelection, service: string, instance: string): boolean {
  if (sel.none) return false
  return isAll(sel) || sel.services.includes(service) || sel.instances.includes(instanceKey(service, instance))
}

export function toggleAll(sel: ApplicationSelection): ApplicationSelection {
  return isAll(sel) ? NO_APPLICATIONS : ALL_APPLICATIONS
}

function explicit(sel: ApplicationSelection, options: ServiceInstancesDto[]): ApplicationSelection {
  if (sel.none) return { services: [], instances: [], none: false }
  if (isAll(sel)) return { services: options.map(o => o.service), instances: [], none: false }
  return { services: [...sel.services], instances: [...sel.instances], none: false }
}

function withoutInstancesOf(keys: string[], option: ServiceInstancesDto): string[] {
  const own = new Set(option.instances.map(i => instanceKey(option.service, i)))
  return keys.filter(k => !own.has(k))
}

export function toggleService(
  sel: ApplicationSelection,
  options: ServiceInstancesDto[],
  service: string
): ApplicationSelection {
  const option = options.find(o => o.service === service)
  if (!option) return sel
  const wasChecked = serviceState(sel, option) === 'checked'
  const next = explicit(sel, options)
  next.instances = withoutInstancesOf(next.instances, option)
  next.services = next.services.filter(s => s !== service)
  if (!wasChecked) next.services.push(service)
  return normalize(next, options)
}

export function toggleInstance(
  sel: ApplicationSelection,
  options: ServiceInstancesDto[],
  service: string,
  instance: string
): ApplicationSelection {
  const option = options.find(o => o.service === service)
  if (!option) return sel
  const key = instanceKey(service, instance)
  const next = explicit(sel, options)
  if (next.services.includes(service)) {
    next.services = next.services.filter(s => s !== service)
    next.instances = [
      ...withoutInstancesOf(next.instances, option),
      ...option.instances.filter(i => i !== instance).map(i => instanceKey(service, i))
    ]
  } else if (next.instances.includes(key)) {
    next.instances = next.instances.filter(k => k !== key)
  } else {
    next.instances = [...next.instances, key]
  }
  return normalize(next, options)
}

export function onlyService(options: ServiceInstancesDto[], service: string): ApplicationSelection {
  return normalize({ services: [service], instances: [], none: false }, options)
}

export function onlyInstance(options: ServiceInstancesDto[], service: string, instance: string): ApplicationSelection {
  return normalize({ services: [], instances: [instanceKey(service, instance)], none: false }, options)
}

/**
 * Canonical form of an explicit selection: fully covered services collapse
 * to their name, every visible service selected collapses to "all", and an
 * empty selection becomes "none".
 */
export function normalize(sel: ApplicationSelection, options: ServiceInstancesDto[]): ApplicationSelection {
  if (sel.none) return NO_APPLICATIONS
  const services = [...new Set(sel.services)]
  let instances = [...new Set(sel.instances)].filter(k => {
    const owner = ownerOf(k, options)
    return owner === null || !services.includes(owner.service)
  })
  for (const option of options) {
    if (option.instances.length === 0 || services.includes(option.service)) continue
    if (option.instances.every(i => instances.includes(instanceKey(option.service, i)))) {
      services.push(option.service)
      instances = withoutInstancesOf(instances, option)
    }
  }
  if (services.length === 0 && instances.length === 0) return NO_APPLICATIONS
  if (options.length > 0 && options.every(o => services.includes(o.service))) return ALL_APPLICATIONS
  return { services, instances, none: false }
}

export type SelectionSummary =
  | { kind: 'all' }
  | { kind: 'none' }
  | { kind: 'service', service: string }
  | { kind: 'instance', service: string, instance: string }
  | { kind: 'instances', service: string, count: number }
  | { kind: 'services', count: number }

export function summarize(sel: ApplicationSelection, options: ServiceInstancesDto[]): SelectionSummary {
  if (sel.none) return { kind: 'none' }
  if (isAll(sel)) return { kind: 'all' }
  const owners = sel.instances.map(k => ownerOf(k, options))
  const touched = new Set([...sel.services, ...owners.map((o, i) => o?.service ?? sel.instances[i]!)])
  if (touched.size === 1) {
    const [service] = touched as Set<string>
    const owner = owners[0]
    if (sel.services.length === 1 || !owner) return { kind: 'service', service: service! }
    if (sel.instances.length === 1) return { kind: 'instance', service: owner.service, instance: owner.instance }
    return { kind: 'instances', service: service!, count: sel.instances.length }
  }
  return { kind: 'services', count: touched.size }
}
