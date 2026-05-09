import type { DemoRouterDeps } from './routes'
import { dispatch } from './routes'
import { demoError } from './state/DashboardStore'

export interface DemoFetcherHooks {
  /** Mirrors the prod fetcher's `onResponseError` interceptor. The plugin
   *  installs the redirect-to-/login handler here so demo and prod
   *  share the same UX. */
  onResponseError?: (ctx: { response: { status: number } }) => void
}

/**
 * Endpoints that don't require auth in the real server. Mirrored here so
 * the demo behaves the same — `/v1/info` serves the app name and is
 * polled by the login page itself, before there's a session.
 */
const PUBLIC_PATHS = new Set<string>(['/v1/info'])

/**
 * sessionStorage key the demo uses to remember "this tab is logged in".
 * Mirrors the role of the real backend's HttpOnly auth cookie — JS would
 * never see that cookie, but in a static-only demo we have no server side
 * to set one, so this is the closest practical analogue. Distinct from
 * <c>AuthStore.StorageKey</c> on purpose: AuthStore tracks the SPA's view
 * ("did I log in?"); this key is the demo's session-of-truth ("does the
 * mock server consider this caller authenticated?"). Two keys → two
 * concerns, like cookie vs. JS state in prod.
 */
const DEMO_SESSION_KEY = 'dashboard.demo.session'

/**
 * Build a function that conforms to the call signature of Nuxt's
 * `$fetch`. We only implement what `HttpClientService` actually invokes —
 * the path + an `{ baseURL, method, query, body, headers }` options bag.
 * The full ofetch surface (`.create()`, `.raw()`, etc.) is stubbed only
 * if the caller pokes at it; everything routes through the call form.
 *
 * In the prod build this module is dead code (the only import lives
 * inside `if (import.meta.env.VITE_DEMO_MODE === 'true')`).
 */
export function createDemoFetcher(
  deps: DemoRouterDeps,
  hooks: DemoFetcherHooks
): typeof $fetch {
  const fn = async (request: string, options?: FetchOptions): Promise<unknown> => {
    const path = stripBaseUrl(request, options?.baseURL)
    const method = (options?.method ?? 'GET').toUpperCase()
    const query = (options?.query ?? {}) as Record<string, unknown>
    const body = options?.body
    // A small artificial latency keeps loading spinners visible; a real
    // network would be ~100ms anyway, this avoids "instant" jank.
    await delay(60)

    // /v1/auth/login and /v1/auth/logout are intercepted before the auth
    // gate — login itself can't be gated, and logout from a stale tab
    // should always succeed. Any non-empty password is accepted: the demo
    // has no real users, the form is just there to mirror the real login
    // UX. Mirrors the real backend's `POST /api/v1/auth/login`, which sets
    // an HttpOnly cookie; here we set a sessionStorage marker instead.
    if (method === 'POST' && path === '/v1/auth/login') {
      const token = (body as { token?: unknown } | null)?.token
      if (typeof token !== 'string' || token.length === 0) {
        throw demoError(400, 'Token is required')
      }
      writeSession(true)
      return undefined
    }
    if (method === 'POST' && path === '/v1/auth/logout') {
      writeSession(false)
      return undefined
    }

    // Auth gate — every other endpoint outside `PUBLIC_PATHS` needs the
    // demo session marker (set by /v1/auth/login above).
    const authenticated = readSession()

    if (!PUBLIC_PATHS.has(path) && !authenticated) {
      const err = demoError(401, 'Authentication required')
      hooks.onResponseError?.({ response: { status: 401 } })
      throw err
    }

    try {
      return dispatch({ method, path, query, body, authenticated }, deps)
    } catch (err) {
      const status = (err as { response?: { status?: number } } | null)?.response?.status
      if (typeof status === 'number') {
        hooks.onResponseError?.({ response: { status } })
      }
      throw err
    }
  }

  // Stub the methods `HttpClientService` doesn't call but `$fetch` exposes,
  // so any unexpected access fails loudly rather than silently swallowing.
  const stubs = {
    create: () => fn,
    raw: async (request: string, options?: FetchOptions) => {
      const data = await fn(request, options)
      return { _data: data, status: 200, headers: new Headers() }
    },
    native: async (request: string, options?: FetchOptions) => {
      const data = await fn(request, options)
      return new Response(JSON.stringify(data ?? null), {
        status: 200,
        headers: { 'content-type': 'application/json' }
      })
    }
  }

  return Object.assign(fn, stubs) as unknown as typeof $fetch
}

interface FetchOptions {
  baseURL?: string
  method?: string
  query?: Record<string, unknown>
  body?: unknown
  headers?: Record<string, string>
}

function stripBaseUrl(request: string, baseURL: string | undefined): string {
  if (baseURL && request.startsWith(baseURL)) {
    return request.slice(baseURL.length) || '/'
  }
  return request
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function readSession(): boolean {
  if (typeof window === 'undefined') return false
  try {
    return window.sessionStorage.getItem(DEMO_SESSION_KEY) === '1'
  } catch {
    return false
  }
}

function writeSession(authenticated: boolean): void {
  if (typeof window === 'undefined') return
  try {
    if (authenticated) {
      window.sessionStorage.setItem(DEMO_SESSION_KEY, '1')
    } else {
      window.sessionStorage.removeItem(DEMO_SESSION_KEY)
    }
  } catch {
    // Safari private mode etc. — fall back to in-memory only; caller
    // ends up logged-out on refresh, acceptable degraded UX for the demo.
  }
}

