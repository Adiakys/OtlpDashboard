/**
 * Persistent width (in pixels) for a named drawer. Each drawer (logs detail,
 * traces detail, …) keeps its own slot keyed by `name`.
 */
export function useDrawerWidth(name: string, defaultWidth = 480, min = 360, max = 0.7) {
  const storageKey = `oteldash-drawer-width-${name}`
  const width = useState<number>(`drawer-width-${name}`, () => {
    if (import.meta.server) return defaultWidth
    const raw = window.localStorage.getItem(storageKey)
    const parsed = raw ? Number.parseInt(raw, 10) : NaN
    return Number.isFinite(parsed) && parsed > 0 ? parsed : defaultWidth
  })

  function setWidth(px: number) {
    const maxPx = import.meta.client ? Math.floor(window.innerWidth * max) : px
    const clamped = Math.min(Math.max(px, min), maxPx || px)
    width.value = clamped
  }

  if (import.meta.client) {
    watch(width, (value) => {
      window.localStorage.setItem(storageKey, String(value))
    })
  }

  return { width, setWidth, min }
}
