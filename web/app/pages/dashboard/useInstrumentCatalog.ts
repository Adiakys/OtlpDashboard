import type { MetricsService } from '~/services/MetricsService'
import type { InstrumentDto } from '~/services/types'
import type { MetricBinding } from './types'
import { STATE_INSTRUMENT_CATALOG } from './composables/stateKeys'

/**
 * Shared, deduped instrument catalog used by every widget on the dashboard
 * page. Centralizes two concerns:
 *
 *  1. **Request fan-out**: many widgets refreshing on the same live tick
 *     would each trigger a `/v1/metrics` call. The shared in-flight promise
 *     collapses concurrent refreshes into a single network request.
 *
 *  2. **Late binding of `resourceHash`**: widget configs may carry a stale
 *     hash (e.g. layout imported from another instance, or instrument was
 *     re-emitted after a restart). {@link resolve} maps a stored binding to
 *     the matching live instrument by logical identity
 *     (`serviceName + scopeName + instrumentName + kind`) — widgets self-heal
 *     as soon as the metric appears in the catalog.
 *
 * State is module-scoped (single MetricsService singleton per app) so the
 * cache survives across composable invocations within the same client run.
 */

interface CatalogState {
  instruments: InstrumentDto[]
  loadedAt: number
  loading: boolean
}

let inflight: Promise<void> | null = null

export function useInstrumentCatalog(metrics: MetricsService) {
  const state = useState<CatalogState>(STATE_INSTRUMENT_CATALOG, () => ({
    instruments: [],
    loadedAt: 0,
    loading: false
  }))

  /**
   * Refresh the catalog. Concurrent callers share the same network request.
   * Failures keep the previous catalog (best-effort: a transient blip
   * shouldn't blank every widget on the page).
   */
  async function refresh(): Promise<void> {
    if (inflight) return inflight
    inflight = (async () => {
      state.value.loading = true
      try {
        state.value.instruments = await metrics.listInstruments()
        state.value.loadedAt = Date.now()
      } catch {
        /* keep stale catalog */
      } finally {
        state.value.loading = false
        inflight = null
      }
    })()
    return inflight
  }

  /** Refresh once if the catalog has never loaded; no-op otherwise. */
  async function ensureLoaded(): Promise<void> {
    if (state.value.loadedAt > 0) return
    return refresh()
  }

  /**
   * Map a stored binding to a binding whose `resourceHash` is guaranteed to
   * point at a live instrument. Returns `null` when no instrument matches —
   * the widget should treat that case as "no data yet" and let the next
   * catalog refresh re-attempt.
   *
   * Match strategy:
   *   1. Fast path — the stored hash + logical key already pin a known
   *      instrument: return the binding unchanged.
   *   2. Slow path — find any instrument with the same logical key and
   *      return a re-bound copy with the live `resourceHash`.
   *
   * `serviceName` is honored when present on the binding; older exports
   * without it fall back to a service-agnostic match rather than failing.
   */
  function resolve(binding: MetricBinding): MetricBinding | null {
    const instruments = state.value.instruments
    if (instruments.length === 0) return null

    const exact = instruments.find(i =>
      i.resourceHash === binding.resourceHash
      && i.scopeName === binding.scopeName
      && i.name === binding.instrumentName
      && i.kind === binding.kind
    )
    if (exact) return binding

    const logical = findByLogicalKey(instruments, binding)
    if (!logical) return null

    return {
      resourceHash: logical.resourceHash,
      scopeName: logical.scopeName,
      instrumentName: logical.name,
      kind: logical.kind,
      serviceName: logical.serviceName,
      unit: logical.unit,
      description: logical.description
    }
  }

  return {
    instruments: computed(() => state.value.instruments),
    isLoading: computed(() => state.value.loading),
    loadedAt: computed(() => state.value.loadedAt),
    refresh,
    ensureLoaded,
    resolve
  }
}

function findByLogicalKey(instruments: InstrumentDto[], binding: MetricBinding): InstrumentDto | null {
  const expectedService = binding.serviceName ?? null
  for (const i of instruments) {
    if (i.scopeName !== binding.scopeName) continue
    if (i.name !== binding.instrumentName) continue
    if (i.kind !== binding.kind) continue
    if (expectedService !== null && i.serviceName !== expectedService) continue
    return i
  }
  return null
}
