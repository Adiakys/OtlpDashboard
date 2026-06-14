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

/** Per-call transport overrides. `signal` lets a caller cancel an in-flight
 *  request (e.g. a superseded filter reload); `timeout` overrides the
 *  client default for the occasional long-running call. */
export interface RequestOptions {
  signal?: AbortSignal
  timeout?: number
}

/** Default per-request timeout. Without it a stalled request leaves the UI
 *  spinning forever (no response, no error) — see the traces-filter bug. */
const DEFAULT_TIMEOUT_MS = 30_000

export class HttpClientService {
  constructor(
    private readonly baseUrl: string,
    private readonly fetcher: Fetcher = $fetch,
    private readonly defaultTimeoutMs: number = DEFAULT_TIMEOUT_MS
  ) {}

  // ofetch only arms its own `timeout` when no `signal` is supplied, so we
  // fold the deadline into the signal ourselves: that keeps the timeout
  // active even on calls that pass an abort signal (the trace-filter
  // reload always does). A timeout fires as a `TimeoutError` (a real
  // failure the caller surfaces); a caller-driven abort stays an
  // `AbortError` the caller can recognise as "superseded, ignore".
  private resolveSignal(options?: RequestOptions): AbortSignal | undefined {
    const timeout = options?.timeout ?? this.defaultTimeoutMs
    if (timeout <= 0) return options?.signal
    const timeoutSignal = AbortSignal.timeout(timeout)
    return options?.signal
      ? AbortSignal.any([options.signal, timeoutSignal])
      : timeoutSignal
  }

  get<T>(path: string, query?: Record<string, unknown>, options?: RequestOptions): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'GET',
      query,
      credentials: 'include',
      signal: this.resolveSignal(options)
    })
  }

  post<T>(path: string, body?: JsonBody, options?: RequestOptions): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'POST',
      body,
      credentials: 'include',
      signal: this.resolveSignal(options)
    })
  }

  put<T>(path: string, body?: JsonBody, options?: RequestOptions): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'PUT',
      body,
      credentials: 'include',
      signal: this.resolveSignal(options)
    })
  }

  delete<T>(path: string, query?: Record<string, unknown>, options?: RequestOptions): Promise<T> {
    return this.fetcher<T>(path, {
      baseURL: this.baseUrl,
      method: 'DELETE',
      query,
      credentials: 'include',
      signal: this.resolveSignal(options)
    })
  }
}
