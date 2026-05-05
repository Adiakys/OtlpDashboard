/**
 * Single decision point for auth-based routing.
 *
 *   /            authed → /dashboard       not authed → /login
 *   /login       authed → /dashboard (or ?next)        — render
 *   /<other>     authed → render             not authed → /login?next=<path>
 *
 * Centralising these rules here means individual pages don't need their
 * own `await navigateTo(...)` in setup (which interacts badly with
 * Suspense and layout transitions during SPA boot under a baseURL
 * subpath). The fetch-level 401 interceptor in `plugins/services.ts`
 * stays as a backstop for tokens that expire mid-session.
 *
 * Reads `document.cookie` directly rather than going through
 * `useNuxtApp().$authStore`. The DI route turned out to be unreliable
 * during chained `replace: true` transitions, where the provider map
 * could still be settling. Cookie access is synchronous and race-free.
 * The envelope shape is mirrored from `AuthStore.ts`; if it changes
 * there, update the type-guard below.
 */
const STORAGE_KEY = 'dashboard.auth'

function readAuthCookie(): string | null {
  if (typeof document === 'undefined') return null
  const prefix = `${encodeURIComponent(STORAGE_KEY)}=`
  for (const part of document.cookie.split('; ')) {
    if (part.startsWith(prefix)) {
      return decodeURIComponent(part.slice(prefix.length))
    }
  }
  return null
}

function isAuthenticated(): boolean {
  const raw = readAuthCookie()
  if (!raw) return false
  try {
    const env = JSON.parse(raw) as { token?: unknown; expiresAt?: unknown }
    return (
      typeof env.token === 'string' &&
      env.token.length > 0 &&
      typeof env.expiresAt === 'number' &&
      env.expiresAt > Date.now()
    )
  } catch {
    return false
  }
}

function safeNext(value: unknown): string {
  return typeof value === 'string' && value.startsWith('/') ? value : '/dashboard'
}

export default defineNuxtRouteMiddleware((to) => {
  if (!import.meta.client) return
  const authed = isAuthenticated()

  if (to.path === '/login') {
    // Already authenticated users skip the login form and land where
    // they were originally headed (or the dashboard).
    if (authed) {
      return navigateTo(safeNext(to.query.next), { replace: true })
    }
    return
  }

  if (to.path === '/') {
    return navigateTo(authed ? '/dashboard' : '/login', { replace: true })
  }

  if (!authed) {
    return navigateTo(
      { path: '/login', query: { next: to.fullPath } },
      { replace: true }
    )
  }
})
