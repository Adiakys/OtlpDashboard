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

  it('defaults to a 30-minute TTL', () => {
    vi.useFakeTimers()
    const start = new Date('2030-01-01T12:00:00Z').getTime()
    vi.setSystemTime(start)

    const store = new AuthStore(inMemoryStorage())
    store.setToken('abc')

    vi.setSystemTime(start + 29 * 60 * 1000)
    expect(store.getToken()).toBe('abc')

    vi.setSystemTime(start + 30 * 60 * 1000 + 1)
    expect(store.getToken()).toBeNull()
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
