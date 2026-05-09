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
 * stays as a backstop for cookies that expire mid-session.
 *
 * The session token now lives in an HttpOnly cookie set by
 * <c>POST /api/v1/auth/login</c>, so JS can't read it. We fall back to
 * a sessionStorage flag the AuthStore keeps in sync ("did this tab log
 * in successfully?"). The flag is best-effort routing UX — every API
 * call still gets a real 401 from the server when the cookie is
 * missing/expired, and the fetch interceptor handles those.
 */
const FLAG_STORAGE_KEY = 'dashboard.auth.signed-in'

function isAuthenticated(): boolean {
  if (typeof window === 'undefined') return false
  try {
    return window.sessionStorage.getItem(FLAG_STORAGE_KEY) === '1'
  } catch {
    // Safari private mode etc.: pessimistic fallback so the user lands
    // on /login and the cookie can still authenticate the API calls.
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
