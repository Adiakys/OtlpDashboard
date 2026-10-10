import type { ServiceInstancesDto } from '~/services/types'

/**
 * Static services list the demo exposes to every `/services` endpoint
 * (`/v1/metrics/services`, `/v1/logs/services`, `/v1/traces/services`);
 * logs and traces pair each name with its instances.
 *
 * Mirrors the docker-compose test stack the project ships under `demo/`,
 * so a viewer pointing at the static demo sees the same names that show
 * up when running the real stack locally.
 */
export const DEMO_SERVICES: readonly string[] = [
  'sample-client',
  'sample-server',
  'postgresql',
  'redis'
] as const

/** Fake `service.instance.id` values so the demo shows the instance rows of
 *  the Applications filter; services not listed report no instance. */
const DEMO_INSTANCES: Readonly<Record<string, readonly string[]>> = {
  'sample-server': ['sample-server-7f9c4d-2xkqp', 'sample-server-7f9c4d-9hzmw', 'sample-server-7f9c4d-lr5tn'],
  'sample-client': ['sample-client-0']
}

export function demoServiceInstances(): ServiceInstancesDto[] {
  return [...DEMO_SERVICES].sort().map(service => ({ service, instances: [...(DEMO_INSTANCES[service] ?? [])] }))
}

/** Stable instance for a generated row, picked from a hash of `seed`. */
export function demoInstanceOf(service: string, seed: string): string | null {
  const instances = DEMO_INSTANCES[service]
  if (!instances || instances.length === 0) return null
  let hash = 0
  for (let i = 0; i < seed.length; i++) hash = (hash * 31 + seed.charCodeAt(i)) | 0
  return instances[Math.abs(hash) % instances.length]!
}
