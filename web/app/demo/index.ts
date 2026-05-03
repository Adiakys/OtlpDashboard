/**
 * Public surface of the demo module. Loaded dynamically from the
 * services plugin only when `import.meta.env.VITE_DEMO_MODE === 'true'`,
 * which means the entire subtree (generators, fixtures, routes, the
 * fetcher, the storage abstraction) is dead-code-eliminated from the
 * production bundle.
 *
 * To flip the demo to localStorage-backed persistence later, change the
 * `new InMemoryStorage()` line below to
 * `new LocalStorageStorage('oteldash-demo:')` — no other site changes.
 *
 * Auth: the demo *keeps* the login flow intact. The demo fetcher
 * inspects the Authorization header and returns 401 when no token is
 * present, exactly like the real backend. Any non-empty password works
 * — it's stored as a bearer token and accepted by the fetcher on
 * subsequent calls.
 */
import { createDemoFetcher, type DemoFetcherHooks } from './fetcher'
import { InMemoryStorage } from './storage/InMemoryStorage'
import { DashboardStore } from './state/DashboardStore'
import { WidgetDefinitionStore } from './state/WidgetDefinitionStore'

export interface DemoBootstrap {
  fetcher: typeof $fetch
}

export function createDemoBootstrap(hooks: DemoFetcherHooks): DemoBootstrap {
  const storage = new InMemoryStorage()
  const dashboards = new DashboardStore(storage)
  const widgets = new WidgetDefinitionStore(storage)
  const fetcher = createDemoFetcher({ dashboards, widgets }, hooks)
  return { fetcher }
}
