import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AuthStore, type TokenStorage } from '~/services/AuthStore'

function inMemoryStorage(): TokenStorage {
  const store = new Map<string, string>()
  return {
    getItem: k => store.get(k) ?? null,
    setItem: (k, v) => { store.set(k, v) },
    removeItem: k => { store.delete(k) }
  }
}

describe('AuthStore', () => {
  beforeEach(() => {
    vi.useRealTimers()
  })

  it('round-trips a token within TTL', () => {
    const store = new AuthStore(inMemoryStorage())

    store.setToken('abc')

    expect(store.getToken()).toBe('abc')
    expect(store.isAuthenticated()).toBe(true)
  })

  it('returns null and auto-clears after TTL elapses', () => {
    vi.useFakeTimers()
    const start = new Date('2030-01-01T12:00:00Z').getTime()
    vi.setSystemTime(start)

    const storage = inMemoryStorage()
    const store = new AuthStore(storage)
    store.setToken('abc', 1_000) // 1s TTL for the test

    vi.setSystemTime(start + 1_001)

    expect(store.getToken()).toBeNull()
    expect(storage.getItem('dashboard.auth')).toBeNull() // auto-cleared
  })

  it('defaults to a 30-minute TTL when idle', () => {
    vi.useFakeTimers()
    const start = new Date('2030-01-01T12:00:00Z').getTime()
    vi.setSystemTime(start)

    const store = new AuthStore(inMemoryStorage())
    store.setToken('abc')

    // No reads in between — the sliding refresh never fires, so the
    // initial 30-minute deadline is the one that takes the token out.
    vi.setSystemTime(start + 30 * 60 * 1000 + 1)
    expect(store.getToken()).toBeNull()
  })

  it('slides the deadline forward on a read past the half-TTL mark', () => {
    vi.useFakeTimers()
    const start = new Date('2030-01-01T12:00:00Z').getTime()
    vi.setSystemTime(start)

    const store = new AuthStore(inMemoryStorage())
    store.setToken('abc') // expiresAt = start + 30min

    // 20 min in — past the half-TTL threshold (15 min). Reading the
    // token here should slide the deadline to "now + 30 min" = 50 min.
    vi.setSystemTime(start + 20 * 60 * 1000)
    expect(store.getToken()).toBe('abc')

    // 35 min in — past the original 30-min deadline, but the slide
    // pushed it to 50 min. Token must still be valid.
    vi.setSystemTime(start + 35 * 60 * 1000)
    expect(store.getToken()).toBe('abc')

    // 51 min in — past the slid 50-min deadline AND no further read
    // happened to slide again (we're checking the boundary). Expired.
    vi.setSystemTime(start + 51 * 60 * 1000)
    expect(store.getToken()).toBeNull()
  })

  it('does not slide on a read inside the first half of the TTL', () => {
    vi.useFakeTimers()
    const start = new Date('2030-01-01T12:00:00Z').getTime()
    vi.setSystemTime(start)

    const storage = inMemoryStorage()
    const store = new AuthStore(storage)
    store.setToken('abc')

    const before = storage.getItem('dashboard.auth')

    // 5 min in — well inside the first half. The cookie/envelope must
    // be unchanged: re-writing on every read would be wasteful for a
    // read-heavy SPA.
    vi.setSystemTime(start + 5 * 60 * 1000)
    expect(store.getToken()).toBe('abc')

    expect(storage.getItem('dashboard.auth')).toBe(before)
  })

  it('clear() removes the token', () => {
    const store = new AuthStore(inMemoryStorage())
    store.setToken('abc')

    store.clear()

    expect(store.getToken()).toBeNull()
    expect(store.isAuthenticated()).toBe(false)
  })

  it('setToken("") clears the token', () => {
    const store = new AuthStore(inMemoryStorage())
    store.setToken('abc')

    store.setToken('')

    expect(store.getToken()).toBeNull()
  })

  it('getToken returns null when storage is empty', () => {
    const store = new AuthStore(inMemoryStorage())

    expect(store.getToken()).toBeNull()
  })

  it('corrupted storage entry is dropped and returns null', () => {
    const storage = inMemoryStorage()
    storage.setItem('dashboard.auth', '{not-valid-json')
    const store = new AuthStore(storage)

    expect(store.getToken()).toBeNull()
    expect(storage.getItem('dashboard.auth')).toBeNull()
  })
})
