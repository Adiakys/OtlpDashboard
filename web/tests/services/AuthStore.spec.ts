import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthStore } from '~/services/AuthStore'
import type { HttpClientService } from '~/services/HttpClientService'

function inMemoryStorage(): Storage {
  const store = new Map<string, string>()
  return {
    get length(): number { return store.size },
    clear: () => store.clear(),
    getItem: k => store.get(k) ?? null,
    setItem: (k, v) => { store.set(k, v) },
    removeItem: k => { store.delete(k) },
    key: i => Array.from(store.keys())[i] ?? null
  }
}

function fakeHttp(): HttpClientService & { calls: Array<{ method: string; path: string; body?: unknown }> } {
  const calls: Array<{ method: string; path: string; body?: unknown }> = []
  return {
    calls,
    get: vi.fn(async () => undefined as never),
    post: vi.fn(async (path, body) => {
      calls.push({ method: 'POST', path, body })
      return undefined as never
    }),
    put: vi.fn(async () => undefined as never),
    delete: vi.fn(async () => undefined as never)
  } as unknown as HttpClientService & { calls: Array<{ method: string; path: string; body?: unknown }> }
}

describe('AuthStore', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('starts logged-out when storage is empty', () => {
    const store = new AuthStore(fakeHttp(), inMemoryStorage())

    expect(store.isAuthenticated()).toBe(false)
  })

  it('login posts the token to /v1/auth/login and flips the flag on success', async () => {
    const http = fakeHttp()
    const storage = inMemoryStorage()
    const store = new AuthStore(http, storage)

    await store.login('s3cret')

    expect(http.calls).toEqual([{ method: 'POST', path: '/v1/auth/login', body: { token: 's3cret' } }])
    expect(store.isAuthenticated()).toBe(true)
    expect(storage.getItem('dashboard.auth.signed-in')).toBe('1')
  })

  it('login propagates failure and leaves the store logged-out', async () => {
    const http = fakeHttp()
    vi.spyOn(http, 'post').mockRejectedValueOnce(new Error('401'))
    const store = new AuthStore(http, inMemoryStorage())

    await expect(store.login('wrong')).rejects.toThrow('401')
    expect(store.isAuthenticated()).toBe(false)
  })

  it('logout calls /v1/auth/logout and clears the flag even if the call fails', async () => {
    const http = fakeHttp()
    const storage = inMemoryStorage()
    const store = new AuthStore(http, storage)
    await store.login('s3cret')

    vi.spyOn(http, 'post').mockRejectedValueOnce(new Error('network'))
    await store.logout()

    expect(store.isAuthenticated()).toBe(false)
    expect(storage.getItem('dashboard.auth.signed-in')).toBeNull()
  })

  it('clear flips the flag without hitting the network', () => {
    const http = fakeHttp()
    const storage = inMemoryStorage()
    storage.setItem('dashboard.auth.signed-in', '1')
    const store = new AuthStore(http, storage)
    expect(store.isAuthenticated()).toBe(true)

    store.clear()

    expect(store.isAuthenticated()).toBe(false)
    expect(http.calls.length).toBe(0)
  })

  it('rehydrates the signed-in flag from existing storage on construction', () => {
    const storage = inMemoryStorage()
    storage.setItem('dashboard.auth.signed-in', '1')

    const store = new AuthStore(fakeHttp(), storage)

    expect(store.isAuthenticated()).toBe(true)
  })

  it('rejects an empty login token without contacting the server', async () => {
    const http = fakeHttp()
    const store = new AuthStore(http, inMemoryStorage())

    await expect(store.login('')).rejects.toThrow()
    expect(http.calls.length).toBe(0)
  })
})
