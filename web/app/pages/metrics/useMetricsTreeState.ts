/**
 * Persistent set of expanded branch paths for the metrics tree, keyed by
 * service so switching applications resets the view to a clean state. Backed
 * by `sessionStorage` — survives a tab refresh but not a tab close, mirroring
 * the user's perceived "this session's view".
 */
export function useMetricsTreeState() {
  const expanded = useState<Set<string>>('metrics-tree-expanded', () => new Set())
  let lastService: string | null = null

  function storageKey(service: string | null): string {
    return `oteldash-metrics-tree-${service ?? '__none__'}`
  }

  function load(service: string | null): Set<string> {
    if (import.meta.server) return new Set()
    try {
      const raw = window.sessionStorage.getItem(storageKey(service))
      if (!raw) return new Set()
      const parsed = JSON.parse(raw) as unknown
      if (!Array.isArray(parsed)) return new Set()
      return new Set(parsed.filter((v): v is string => typeof v === 'string'))
    } catch {
      return new Set()
    }
  }

  function persist(service: string | null, value: Set<string>) {
    if (import.meta.server) return
    try {
      window.sessionStorage.setItem(storageKey(service), JSON.stringify([...value]))
    } catch {
      // Storage quota exceeded or sessionStorage unavailable — ignore silently.
    }
  }

  function bind(service: string | null) {
    if (lastService === service) return
    lastService = service
    expanded.value = load(service)
  }

  function isExpanded(path: string): boolean {
    return expanded.value.has(path)
  }

  function toggle(path: string) {
    const next = new Set(expanded.value)
    if (next.has(path)) next.delete(path)
    else next.add(path)
    expanded.value = next
    persist(lastService, next)
  }

  function expandAll(paths: string[]) {
    const next = new Set(expanded.value)
    for (const p of paths) next.add(p)
    expanded.value = next
    persist(lastService, next)
  }

  function collapseAll() {
    expanded.value = new Set()
    persist(lastService, expanded.value)
  }

  return { expanded, bind, isExpanded, toggle, expandAll, collapseAll }
}
