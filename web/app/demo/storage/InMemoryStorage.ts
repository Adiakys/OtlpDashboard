import type { StorageService } from './StorageService'

/**
 * Default demo store: a Map living inside the JS heap. Reset on every page
 * reload, which is fine — the demo is meant to feel like a sandbox, not a
 * persistent account. Edits stick within a tab session so dashboard
 * authoring still feels responsive.
 */
export class InMemoryStorage implements StorageService {
  private readonly map = new Map<string, unknown>()

  get<T>(key: string): T | null {
    return (this.map.get(key) as T | undefined) ?? null
  }

  set<T>(key: string, value: T): void {
    this.map.set(key, value)
  }

  remove(key: string): void {
    this.map.delete(key)
  }

  keys(): string[] {
    return [...this.map.keys()]
  }
}
