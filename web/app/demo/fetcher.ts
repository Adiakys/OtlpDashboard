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
 * polled by the login page itself, before there's a token.
 */
const PUBLIC_PATHS = new Set<string>(['/v1/info'])

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

    // Auth gate — every endpoint outside `PUBLIC_PATHS` needs a bearer
    // token. The Authorization header is set by HttpClientService when a
    // token exists; we inspect it directly rather than reading the auth
    // store so the contract stays "what the wire shows, the demo
    // validates".
    const auth = options?.headers?.['Authorization'] ?? options?.headers?.['authorization']
    const token = typeof auth === 'string' && auth.startsWith('Bearer ') ? auth.slice(7) : ''
    const authenticated = token.length > 0

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

