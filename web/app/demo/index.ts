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
 * intercepts <c>POST /v1/auth/login</c> / <c>...auth/logout</c> and
 * tracks the session via a <c>sessionStorage</c> marker — the closest
 * static-build analogue to the real backend's HttpOnly cookie. Any
 * non-empty password is accepted; the form is purely for UX parity with
 * the real login. Once authenticated the marker survives reloads of
 * the same tab; closing the tab logs out.
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
