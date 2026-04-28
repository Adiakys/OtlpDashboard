/**
 * Persistence contract for the auth token. Abstracted so unit tests can
 * inject an in-memory stub without touching `localStorage`.
 */
export interface TokenStorage {
  getItem(key: string): string | null
  setItem(key: string, value: string): void
  removeItem(key: string): void
}

interface TokenEnvelope {
  token: string
  expiresAt: number
}

/**
 * Single source of truth for the auth bearer token used by the read-API.
 * Two entry points populate it:
 *  - URL landing: `?token=…` captured by the services plugin.
 *  - (Future) login page: stores the user-entered token via `setToken`.
 *
 * Consumers read via `getToken()` which transparently returns `null` when the
 * token has expired, keeping call sites unaware of the persistence format.
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
      expiresAt: Date.now() + ttlMs
    }
    this.storage.setItem(AuthStore.StorageKey, JSON.stringify(envelope))
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

    return envelope.token
  }

  clear(): void {
    this.storage.removeItem(AuthStore.StorageKey)
  }

  isAuthenticated(): boolean {
    return this.getToken() !== null
  }
}

function defaultStorage(): TokenStorage {
  if (typeof window !== 'undefined' && typeof window.localStorage !== 'undefined') {
    return window.localStorage
  }
  // SSR / node contexts (e.g. tests that don't inject): behave like an
  // empty, write-through-nowhere store.
  return {
    getItem: () => null,
    setItem: () => {},
    removeItem: () => {}
  }
}
