/**
 * Persistent split ratio in [0,1] (left or top pane size). Each split keeps
 * its own slot keyed by `name`.
 */
export function useSplitRatio(name: string, defaultRatio = 0.5, min = 0.15, max = 0.85) {
  const storageKey = `oteldash-split-ratio-${name}`
  const ratio = useState<number>(`split-ratio-${name}`, () => {
    if (import.meta.server) return defaultRatio
    const raw = window.localStorage.getItem(storageKey)
    const parsed = raw ? Number.parseFloat(raw) : NaN
    return Number.isFinite(parsed) && parsed > 0 && parsed < 1 ? parsed : defaultRatio
  })

  function setRatio(value: number) {
    ratio.value = Math.min(Math.max(value, min), max)
  }

  if (import.meta.client) {
    watch(ratio, (value) => {
      window.localStorage.setItem(storageKey, value.toFixed(4))
    })
  }

  return { ratio, setRatio, min, max }
}
