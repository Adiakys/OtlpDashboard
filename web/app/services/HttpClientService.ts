import type { $Fetch } from 'ofetch'

/**
 * Thin wrapper around ofetch (Nuxt's `$fetch`). Exposes explicit GET/POST/PUT/
 * DELETE verbs so the feature services stay vocabulary-consistent and can be
 * unit-tested against a stubbed fetcher.
 *
 * If <paramref name="getToken"/> returns a non-empty string, every request
 * gains an `Authorization: Bearer <token>` header. The token provider is
 * injected (not stored) so the client never caches stale values — it always
 * asks the AuthStore at call time.
 *
 * Constructed once at app startup by `plugins/services.ts` and injected into
 * `LogsService` and `TraceService` as a shared singleton.
 */
export class HttpClientService {
  constructor(
    private readonly baseUrl: string,
    private readonly getToken: () => string | null = () => null,
    private readonly fetcher: $Fetch = $fetch
  ) {}

  get<T>(path: string, query?: Record<string, unknown>): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'GET',
      query,
      headers: this.authHeaders()
    })
  }

  post<T>(path: string, body?: unknown): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'POST',
      body,
      headers: this.authHeaders()
    })
  }

  put<T>(path: string, body?: unknown): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'PUT',
      body,
      headers: this.authHeaders()
    })
  }

  delete<T>(path: string, query?: Record<string, unknown>): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'DELETE',
      query,
      headers: this.authHeaders()
    })
  }

  private authHeaders(): Record<string, string> {
    const token = this.getToken()
    return token ? { Authorization: `Bearer ${token}` } : {}
  }
}
