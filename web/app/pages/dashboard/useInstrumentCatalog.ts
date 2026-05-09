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
 *     (`serviceName + scopeName + instrumentName + kind` plus optional
 *     `service.instance.id`) — widgets self-heal as soon as the metric
 *     appears in the catalog.
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

/**
 * Outcome of a {@link resolve} call.
 *
 * - `resolved`: the binding pins exactly one live instrument; the
 *   returned `binding` carries the live `resourceHash`.
 * - `ambiguous`: the logical key matched more than one instrument and
 *   either no `serviceInstanceId` was pinned, or the pinned id is not
 *   present. Widgets render a warning instead of arbitrarily picking
 *   one of the available instances. `available` lists the
 *   `serviceInstanceId` values the user can choose from.
 * - `no-match`: zero instruments match the logical key. Widgets show
 *   their normal "no data" empty state — typical for a fresh dashboard
 *   where the metric hasn't been emitted yet.
 */
export type Resolution =
  | { kind: 'resolved'; binding: MetricBinding }
  | { kind: 'ambiguous'; requestedId: string | null; available: string[] }
  | { kind: 'no-match' }

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
   * Map a stored binding to a live instrument with three explicit outcomes
   * (see {@link Resolution}). Resolution rules:
   *
   *  1. Filter the catalog by the logical key
   *     `(scopeName, instrumentName, kind, serviceName)`.
   *  2. If zero matches: `no-match`.
   *  3. If `serviceInstanceId` is pinned on the binding:
   *     - exact match found → `resolved`
   *     - no exact match     → `ambiguous` (the configured id is gone)
   *  4. If `serviceInstanceId` is unset:
   *     - exactly one match  → `resolved` (single-instance services keep
   *                            working without explicit pinning)
   *     - more than one match → `ambiguous` (forces the user to pick one)
   *
   * The "ambiguous" branch deliberately refuses to silently pick the
   * first match — that was the bug the explicit instance pin is here to
   * fix. Widgets render a non-blocking warning instead.
   */
  function resolve(binding: MetricBinding): Resolution {
    const instruments = state.value.instruments
    if (instruments.length === 0) return { kind: 'no-match' }

    const expectedService = binding.serviceName ?? null
    const expectedInstance = binding.serviceInstanceId ?? null

    const matches: InstrumentDto[] = []
    for (const i of instruments) {
      if (i.scopeName !== binding.scopeName) continue
      if (i.name !== binding.instrumentName) continue
      if (i.kind !== binding.kind) continue
      if (expectedService !== null && i.serviceName !== expectedService) continue
      matches.push(i)
    }

    if (matches.length === 0) return { kind: 'no-match' }

    if (expectedInstance !== null) {
      const exact = matches.find(i => i.serviceInstanceId === expectedInstance)
      if (exact) return { kind: 'resolved', binding: bindingFromInstrument(exact) }
      return {
        kind: 'ambiguous',
        requestedId: expectedInstance,
        available: collectInstanceIds(matches)
      }
    }

    if (matches.length === 1) {
      return { kind: 'resolved', binding: bindingFromInstrument(matches[0]!) }
    }

    return {
      kind: 'ambiguous',
      requestedId: null,
      available: collectInstanceIds(matches)
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

/** Project an InstrumentDto back into a MetricBinding shape. Carries
 *  `serviceInstanceId` along so consumers can render it. */
function bindingFromInstrument(i: InstrumentDto): MetricBinding {
  return {
    resourceHash: i.resourceHash,
    scopeName: i.scopeName,
    instrumentName: i.name,
    kind: i.kind,
    serviceName: i.serviceName,
    serviceInstanceId: i.serviceInstanceId,
    unit: i.unit,
    description: i.description
  }
}

/** Stable, deduped list of non-null `serviceInstanceId`s, sorted for a
 *  deterministic warning message. */
function collectInstanceIds(matches: InstrumentDto[]): string[] {
  const ids = new Set<string>()
  for (const m of matches) {
    if (m.serviceInstanceId) ids.add(m.serviceInstanceId)
  }
  return [...ids].sort()
}
