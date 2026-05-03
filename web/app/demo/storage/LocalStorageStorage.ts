import type { StorageService } from './StorageService'

/**
 * Swap-ready alternative to `InMemoryStorage`. Wired but unused by default —
 * to enable, change the storage construction in `demo/index.ts` to
 * `new LocalStorageStorage('oteldash-demo:')`. Demo mutations would then
 * survive page reloads at the cost of accumulating cruft in the user's
 * browser; default is in-memory so the demo is deterministic per visit.
 */
export class LocalStorageStorage implements StorageService {
  constructor(private readonly prefix: string = 'oteldash-demo:') {}

  get<T>(key: string): T | null {
    if (typeof window === 'undefined') return null
    const raw = window.localStorage.getItem(this.prefix + key)
    if (raw === null) return null
    try {
      return JSON.parse(raw) as T
    } catch {
      return null
    }
  }

  set<T>(key: string, value: T): void {
    if (typeof window === 'undefined') return
    window.localStorage.setItem(this.prefix + key, JSON.stringify(value))
  }

  remove(key: string): void {
    if (typeof window === 'undefined') return
    window.localStorage.removeItem(this.prefix + key)
  }

  keys(): string[] {
    if (typeof window === 'undefined') return []
    const out: string[] = []
    for (let i = 0; i < window.localStorage.length; i++) {
      const k = window.localStorage.key(i)
      if (k && k.startsWith(this.prefix)) out.push(k.slice(this.prefix.length))
    }
    return out
  }
}
