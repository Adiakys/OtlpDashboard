import type { InstrumentDto } from '~/services/types'
import { instrumentKey } from './buildTree'

const STORAGE_KEY = 'oteldash-metrics-selection'

interface PersistedEntry {
  key: string
  resourceHash: string
  scopeName: string
  name: string
  kind: string
}

/**
 * Multi-selection of instruments constrained to share the same `kind` (so the
 * chart picks one representation). Reads/writes `sessionStorage` so the
 * selection survives a refresh but not the tab.
 */
export function useMetricsSelection() {
  const selectedKeys = useState<Set<string>>('metrics-selected-keys', () => new Set())
  // Snapshot of the last seen instrument metadata for each selected key, so we
  // can answer `selectedInstruments` even before the next list refresh.
  const selectedSnapshots = useState<Map<string, InstrumentDto>>(
    'metrics-selected-snapshots',
    () => new Map()
  )

  if (import.meta.client && selectedKeys.value.size === 0) {
    rehydrate(selectedKeys.value, selectedSnapshots.value)
  }

  const selectedKind = computed<string | null>(() => {
    for (const k of selectedKeys.value) {
      const snap = selectedSnapshots.value.get(k)
      if (snap) return snap.kind
    }
    return null
  })

  function isSelected(instrument: InstrumentDto): boolean {
    return selectedKeys.value.has(instrumentKey(instrument))
  }

  function isCompatible(instrument: InstrumentDto): boolean {
    const current = selectedKind.value
    return current === null || current === instrument.kind
  }

  function toggle(instrument: InstrumentDto): boolean {
    const key = instrumentKey(instrument)
    const nextKeys = new Set(selectedKeys.value)
    const nextSnaps = new Map(selectedSnapshots.value)

    if (nextKeys.has(key)) {
      nextKeys.delete(key)
      nextSnaps.delete(key)
    } else {
      if (selectedKind.value !== null && selectedKind.value !== instrument.kind) {
        return false
      }
      nextKeys.add(key)
      nextSnaps.set(key, instrument)
    }

    selectedKeys.value = nextKeys
    selectedSnapshots.value = nextSnaps
    persist(nextSnaps)
    return true
  }

  function remove(key: string) {
    if (!selectedKeys.value.has(key)) return
    const nextKeys = new Set(selectedKeys.value)
    const nextSnaps = new Map(selectedSnapshots.value)
    nextKeys.delete(key)
    nextSnaps.delete(key)
    selectedKeys.value = nextKeys
    selectedSnapshots.value = nextSnaps
    persist(nextSnaps)
  }

  function clear() {
    if (selectedKeys.value.size === 0) return
    selectedKeys.value = new Set()
    selectedSnapshots.value = new Map()
    persist(selectedSnapshots.value)
  }

  /** Drop selections whose key is no longer present in the list — typically
   *  after an application switch or a long absence from the source process. */
  function reconcile(known: InstrumentDto[]) {
    const knownKeys = new Set(known.map(instrumentKey))
    let changed = false
    const nextKeys = new Set(selectedKeys.value)
    const nextSnaps = new Map(selectedSnapshots.value)
    for (const k of selectedKeys.value) {
      if (!knownKeys.has(k)) {
        nextKeys.delete(k)
        nextSnaps.delete(k)
        changed = true
      }
    }
    if (changed) {
      selectedKeys.value = nextKeys
      selectedSnapshots.value = nextSnaps
      persist(nextSnaps)
    }
    // Refresh snapshots from the latest list so units/temporality stay current.
    for (const i of known) {
      const k = instrumentKey(i)
      if (selectedKeys.value.has(k)) selectedSnapshots.value.set(k, i)
    }
  }

  const selectedInstruments = computed<InstrumentDto[]>(() => {
    const out: InstrumentDto[] = []
    for (const k of selectedKeys.value) {
      const snap = selectedSnapshots.value.get(k)
      if (snap) out.push(snap)
    }
    return out
  })

  return {
    selectedKeys,
    selectedKind,
    selectedInstruments,
    isSelected,
    isCompatible,
    toggle,
    remove,
    clear,
    reconcile
  }
}

function persist(snapshots: Map<string, InstrumentDto>) {
  if (import.meta.server) return
  try {
    const entries: PersistedEntry[] = []
    for (const [key, i] of snapshots) {
      entries.push({
        key,
        resourceHash: i.resourceHash,
        scopeName: i.scopeName,
        name: i.name,
        kind: i.kind
      })
    }
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(entries))
  } catch {
    // ignore quota / private mode errors
  }
}

function rehydrate(keys: Set<string>, snaps: Map<string, InstrumentDto>) {
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return
    const parsed = JSON.parse(raw) as unknown
    if (!Array.isArray(parsed)) return
    for (const e of parsed as PersistedEntry[]) {
      if (!e || typeof e.key !== 'string') continue
      keys.add(e.key)
      // Reconstruct a partial DTO; missing fields will refresh on next reload.
      snaps.set(e.key, {
        resourceHash: e.resourceHash,
        scopeName: e.scopeName,
        name: e.name,
        kind: e.kind,
        serviceName: null,
        serviceInstanceId: null,
        description: null,
        unit: null,
        isMonotonic: false,
        temporality: 'Unspecified',
        pointCount: 0
      })
    }
  } catch {
    // ignore
  }
}
