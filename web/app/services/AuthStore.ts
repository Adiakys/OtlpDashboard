import type { HttpClientService } from './HttpClientService'

/**
 * Auth state for the SPA. The actual session token now lives in an
 * <c>HttpOnly</c> cookie set by <c>POST /api/v1/auth/login</c>; the
 * browser ships it automatically on every request, JS never sees it.
 *
 * What we track in JS is just the boolean "have we successfully signed
 * in this tab?" The server is the source of truth: a 401 from any API
 * call clears the flag and bounces the user to <c>/login</c>.
 *
 * Persisted in <c>sessionStorage</c> so the flag survives reloads of an
 * authenticated tab; tabs without a known-good login start as logged-out
 * even when the cookie is still present (a fresh navigation always
 * round-trips through the read API anyway, so the first 401 will
 * recover correctly).
 */
export class AuthStore {
  private static readonly StorageKey = 'dashboard.auth.signed-in'

  private signedIn: boolean
  private readonly storage: Storage | null

  constructor(http: HttpClientService, storage?: Storage) {
    this.http = http
    this.storage = storage ?? readableStorage()
    this.signedIn = this.storage?.getItem(AuthStore.StorageKey) === '1'
  }

  private readonly http: HttpClientService

  /**
   * Exchange the password for an HttpOnly session cookie. Throws on
   * non-204 responses; callers translate that into a user-facing
   * "wrong password" message.
   */
  async login(token: string): Promise<void> {
    if (!token) {
      throw new Error('token is required')
    }
    await this.http.post('/v1/auth/login', { token })
    this.signedIn = true
    this.storage?.setItem(AuthStore.StorageKey, '1')
  }

  /**
   * Tear down the server-side session and the local flag. Best-effort:
   * a failure to reach /logout (offline, server crash) still clears the
   * SPA state so the user lands on the login screen — the cookie may
   * survive on the server, but the SPA will treat itself as logged-out
   * and the user can re-attempt logout from a fresh login.
   */
  async logout(): Promise<void> {
    try {
      await this.http.post('/v1/auth/logout')
    } catch {
      // Swallow: best-effort on this side.
    } finally {
      this.clear()
    }
  }

  /** Mark the tab as logged-out without round-tripping the server. Used
   *  by the 401 interceptor and as a safety net inside <c>logout</c>. */
  clear(): void {
    this.signedIn = false
    this.storage?.removeItem(AuthStore.StorageKey)
  }

  isAuthenticated(): boolean {
    return this.signedIn
  }
}

function readableStorage(): Storage | null {
  if (typeof window === 'undefined') return null
  try {
    return window.sessionStorage
  } catch {
    // Safari private mode etc.
    return null
  }
}
