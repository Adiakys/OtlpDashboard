/**
 * Persistence contract for the auth token. Abstracted so unit tests can
 * inject an in-memory stub without touching `document.cookie`. The
 * optional <c>ttlMs</c> on <see cref="setItem"/> lets the production
 * cookie-backed impl set a matching <c>Max-Age</c> so the browser scrubs
 * the cookie on its own; in-memory stubs ignore it.
 */
export interface TokenStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string, ttlMs?: number): void
  removeItem(key: string): void
}

interface TokenEnvelope {
  token: string
  expiresAt: number
  /** Original TTL (ms) used when the envelope was written. Carried so
   *  the sliding-refresh in <see cref="AuthStore.getToken"/> can reset
   *  the deadline to "now + ttlMs" without losing custom-TTL callers. */
  ttlMs?: number
}

/**
 * Single source of truth for the auth bearer token used by the read-API.
 * Backed by a JS-readable cookie with <c>Max-Age</c> matching the chosen
 * TTL — the browser deletes the cookie itself when it expires, which
 * makes the timeout robust against stale tabs / clock manipulation in
 * the SPA's read path.
 *
 * The envelope (<c>{token, expiresAt}</c>) is preserved as defence in
 * depth: even if a future caller bypasses Max-Age (e.g. an in-memory
 * stub), <see cref="getToken"/> still enforces the deadline.
 */
export class AuthStore {
  private static readonly StorageKey = 'dashboard.auth'
  private static readonly DefaultTtlMs = 30 * 60 * 1000 // 30 minutes

  constructor(private readonly storage: TokenStorage = defaultStorage()) {}

  setToken(token: string, ttlMs = AuthStore.DefaultTtlMs): void {
    if (!token) {
      this.clear()
      return
    }
    const envelope: TokenEnvelope = {
      token,
      expiresAt: Date.now() + ttlMs,
      ttlMs
    }
    this.storage.setItem(AuthStore.StorageKey, JSON.stringify(envelope), ttlMs)
  }

  getToken(): string | null {
    const raw = this.storage.getItem(AuthStore.StorageKey)
    if (!raw) return null

    let envelope: TokenEnvelope
    try {
      envelope = JSON.parse(raw) as TokenEnvelope
    } catch {
      // Corrupt entry: drop it silently.
      this.storage.removeItem(AuthStore.StorageKey)
      return null
    }

    if (typeof envelope.token !== 'string' || typeof envelope.expiresAt !== 'number') {
      this.storage.removeItem(AuthStore.StorageKey)
      return null
    }

    if (envelope.expiresAt <= Date.now()) {
      this.storage.removeItem(AuthStore.StorageKey)
      return null
    }

    // Sliding expiration: if more than half the TTL has elapsed, push
    // the deadline back to "now + ttl". Every API call goes through
    // here (the HTTP client calls getToken before each request), so an
    // active user keeps their session alive without needing a heartbeat
    // — yet a truly idle tab still expires after one full TTL.
    // Threshold (>= ttl/2 elapsed) avoids re-writing the cookie on
    // every read, which would be wasteful for read-heavy SPAs.
    const ttl = envelope.ttlMs ?? AuthStore.DefaultTtlMs
    if (envelope.expiresAt - Date.now() < ttl / 2) {
      this.setToken(envelope.token, ttl)
    }

    return envelope.token
  }

  clear(): void {
    this.storage.removeItem(AuthStore.StorageKey)
  }

  isAuthenticated(): boolean {
    return this.getToken() !== null
  }
}

/**
 * Cookie-backed storage. <c>Max-Age</c> matches the AuthStore TTL so the
 * browser deletes the cookie on its own — no lazy-read scrubbing
 * required. <c>SameSite=Strict</c> blocks the cookie on cross-site
 * requests; <c>Secure</c> kicks in only on HTTPS so localhost dev still
 * works. <c>HttpOnly</c> is intentionally absent: the SPA reads the
 * token in JS to set the <c>Authorization: Bearer</c> header.
 */
function defaultStorage(): TokenStorage {
  if (typeof document === 'undefined') {
    // SSR / node contexts (e.g. tests that don't inject): behave like an
    // empty, write-through-nowhere store.
    return {
      getItem: () => null,
      setItem: () => {},
      removeItem: () => {}
    }
  }
  return {
    getItem: (key) => readCookie(key),
    setItem: (key, value, ttlMs) => writeCookie(key, value, ttlMs ?? 0),
    // Max-Age=0 tells the browser to delete the cookie immediately. The
    // Path attribute must match the one used on write or the deletion
    // is ignored.
    removeItem: (key) => writeCookie(key, '', 0)
  }
}

function readCookie(name: string): string | null {
  const prefix = `${encodeURIComponent(name)}=`
  // `document.cookie` is the entire jar joined by "; "; we walk it
  // looking for the matching name. No native single-cookie getter exists.
  for (const part of document.cookie.split('; ')) {
    if (part.startsWith(prefix)) {
      return decodeURIComponent(part.slice(prefix.length))
    }
  }
  return null
}

function writeCookie(name: string, value: string, ttlMs: number): void {
  const isSecure = typeof window !== 'undefined' && window.location.protocol === 'https:'
  const attrs = [
    `${encodeURIComponent(name)}=${encodeURIComponent(value)}`,
    // Browsers floor sub-second Max-Age to 0 anyway, so integer seconds.
    `Max-Age=${Math.max(0, Math.floor(ttlMs / 1000))}`,
    'Path=/',
    'SameSite=Strict'
  ]
  if (isSecure) attrs.push('Secure')
  document.cookie = attrs.join('; ')
}
