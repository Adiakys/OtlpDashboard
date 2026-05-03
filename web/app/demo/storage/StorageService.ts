/**
 * Persistence contract for demo-mode mutable state (dashboards the user
 * edits, custom widget definitions). Abstracted so we can flip between
 * an in-memory store (default — feels like a real app for one tab session)
 * and a localStorage-backed store (would persist across reloads) without
 * touching call sites.
 */
export interface StorageService {
  get<T>(key: string): T | null
  set<T>(key: string, value: T): void
  remove(key: string): void
  keys(): string[]
}
