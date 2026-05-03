/**
 * Static services list the demo exposes to every `/services` endpoint
 * (`/v1/metrics/services`, `/v1/logs/services`, `/v1/traces/services`).
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
