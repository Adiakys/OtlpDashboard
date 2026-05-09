/**
 * Thin wrapper around ofetch (Nuxt's `$fetch`). Exposes explicit GET/POST/PUT/
 * DELETE verbs so the feature services stay vocabulary-consistent and can be
 * unit-tested against a stubbed fetcher.
 *
 * Auth travels through an HttpOnly cookie set by `POST /api/v1/auth/login` —
 * the browser attaches it to every request automatically when
 * `credentials: 'include'` is set, and JS never sees the value.
 *
 * Constructed once at app startup by `plugins/services.ts` and injected into
 * the feature services as a shared singleton.
 *
 * The fetcher is typed as `typeof $fetch` (i.e. Nuxt's augmented variant with
 * `native`) rather than ofetch's bare `$Fetch`, so `$fetch.create(...)` from
 * the plugin assigns cleanly without a structural mismatch.
 */
type Fetcher = typeof $fetch

/** Bodies we send are always plain JSON-serializable objects (DTOs). `object`
 *  covers any named-key shape without forcing callers to add an index
 *  signature; ofetch happily serializes whatever we hand it. */
type JsonBody = object

export class HttpClientService {
  constructor(
    private readonly baseUrl: string,
    private readonly fetcher: Fetcher = $fetch
  ) {}

  get<T>(path: string, query?: Record<string, unknown>): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'GET',
      query,
      credentials: 'include'
    })
  }

  post<T>(path: string, body?: JsonBody): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'POST',
      body,
      credentials: 'include'
    })
  }

  put<T>(path: string, body?: JsonBody): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'PUT',
      body,
      credentials: 'include'
    })
  }

  delete<T>(path: string, query?: Record<string, unknown>): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'DELETE',
      query,
      credentials: 'include'
    })
  }
}
