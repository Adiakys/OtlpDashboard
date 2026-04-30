import { computed, ref, type Component, type ComputedRef, type Ref } from 'vue'
import { WIDGET_REGISTRY } from './registry'
import {
  formatKind,
  parseKind,
  type BuiltinKind,
  type FQKind,
  type WidgetConfig,
  type WidgetDefinition,
  type WidgetEngine,
  type WidgetSource
} from './types'
import type { WidgetDefinitionDto, WidgetEngine as WidgetEngineWire } from '~/services/types'

/**
 * Static definitions of every builtin kind, projected into the same
 * `WidgetDefinition` shape the catalog uses for custom and library widgets.
 *
 * The registry (`WIDGET_REGISTRY`) is still the source of truth for Vue
 * component bindings — it carries the `component` / `configForm`. This map
 * is the *data-shaped* mirror: `kind`, `source`, `engine`, `baseKind`,
 * `defaultConfig`. The catalog merges everything below into a single lookup
 * surface so `WidgetSlot` and `WidgetConfigSlot` don't have to know whether
 * a kind came from the bundle, the server, or a filesystem library.
 */
export const STD_DEFINITIONS: Record<BuiltinKind, WidgetDefinition> = (() => {
  const out = {} as Record<BuiltinKind, WidgetDefinition>
  for (const [kind, meta] of Object.entries(WIDGET_REGISTRY) as [BuiltinKind, typeof WIDGET_REGISTRY[BuiltinKind]][]) {
    out[kind] = {
      kind: `std:${kind}`,
      source: 'std',
      // titleKey/descKey come from i18n — `name`/`description` here are the
      // fallback labels surfaced when no translation is available (e.g.
      // outside Vue context, in tests). The picker resolves the i18n keys
      // against `WIDGET_REGISTRY` directly when rendering.
      name: kind,
      description: undefined,
      icon: meta.icon,
      engine: 'preset',
      defaultSize: meta.defaultSize,
      baseKind: kind,
      defaultConfig: meta.defaultConfig()
    }
  }
  return out
})()

/**
 * Turn a `WidgetDefinition` into the Vue component to mount in the grid.
 * Engine dispatch:
 *   - `preset`    → render the wrapped builtin's component.
 *   - `spec`      → render the Vega-Lite host (iter 2 — placeholder).
 *   - `composite` → render the composite host (iter 5 — placeholder).
 */
export function resolveComponent(def: WidgetDefinition | null): Component | null {
  if (def === null) return null
  if (def.engine === 'preset' && def.baseKind) {
    return WIDGET_REGISTRY[def.baseKind].component
  }
  // Engines wired in later iterations — fall back to null so the slot
  // renders the "widget not available" placeholder.
  return null
}

/**
 * Turn a `WidgetDefinition` into the Vue component to mount in the config
 * drawer. For `preset`, the builtin's own form drives the per-instance
 * config; for `spec` / `composite` we'll need bespoke forms (iter 2/5).
 */
export function resolveConfigForm(def: WidgetDefinition | null): Component | null {
  if (def === null) return null
  if (def.engine === 'preset' && def.baseKind) {
    return WIDGET_REGISTRY[def.baseKind].configForm
  }
  return null
}

/**
 * The default config the picker copies into a fresh widget instance of the
 * given definition. For `preset`, this is the definition's `defaultConfig`
 * (a clone, so each instance starts with its own object).
 */
export function defaultConfigForDefinition(def: WidgetDefinition): WidgetConfig {
  if (def.engine === 'preset' && def.defaultConfig) {
    // Structured clone so two instances added back-to-back don't share the
    // same reference. JSON round-trip is safe for our config shapes
    // (no Date / Map / undefined-as-significant).
    return JSON.parse(JSON.stringify(def.defaultConfig))
  }
  // Spec / composite engines fall back to an empty object — the form will
  // populate it at first mount.
  return {} as WidgetConfig
}

/**
 * The catalog: merged view of all widget definitions available to the
 * dashboard, regardless of source. Built from a reactive list of custom
 * definitions (DB-backed) and, in later iterations, a list of library
 * definitions (filesystem / git).
 *
 * Reactive: changes to `customDefinitions` (e.g. after a save) propagate
 * to every consumer via `byKind` / `bySource` / `all`.
 */
export interface WidgetCatalog {
  /** Read-only flat list, std + custom + library in that order. */
  all: ComputedRef<WidgetDefinition[]>
  /** O(1) lookup by FQ kind. Returns `null` for unknown kinds. */
  byKind: (kind: FQKind) => WidgetDefinition | null
  /** Filtered view by source bucket. */
  bySource: (source: 'std' | 'custom' | 'library') => ComputedRef<WidgetDefinition[]>
  /** Library widgets grouped by library id (iter 3). */
  byLibrary: ComputedRef<Map<string, WidgetDefinition[]>>
}

export function buildWidgetCatalog(
  customDefinitions: Ref<WidgetDefinition[]>,
  libraryDefinitions: Ref<WidgetDefinition[]>
): WidgetCatalog {
  const stdList = computed<WidgetDefinition[]>(() => Object.values(STD_DEFINITIONS))

  const all = computed<WidgetDefinition[]>(() => [
    ...stdList.value,
    ...customDefinitions.value,
    ...libraryDefinitions.value
  ])

  const lookup = computed<Map<FQKind, WidgetDefinition>>(() => {
    const map = new Map<FQKind, WidgetDefinition>()
    for (const def of all.value) map.set(def.kind, def)
    return map
  })

  function byKind(kind: FQKind): WidgetDefinition | null {
    // Tolerant lookup: legacy bare-kind values resolve via `formatKind`.
    const direct = lookup.value.get(kind)
    if (direct) return direct
    const parsed = parseKind(kind)
    return lookup.value.get(formatKind(parsed)) ?? null
  }

  const stdBucket = computed<WidgetDefinition[]>(() => stdList.value)
  const customBucket = computed<WidgetDefinition[]>(() => customDefinitions.value)
  const libraryBucket = computed<WidgetDefinition[]>(() => libraryDefinitions.value)

  function bySource(source: 'std' | 'custom' | 'library'): ComputedRef<WidgetDefinition[]> {
    if (source === 'std') return stdBucket
    if (source === 'custom') return customBucket
    return libraryBucket
  }

  const byLibrary = computed<Map<string, WidgetDefinition[]>>(() => {
    const map = new Map<string, WidgetDefinition[]>()
    for (const def of libraryDefinitions.value) {
      const src = def.source
      if (typeof src !== 'object') continue
      const list = map.get(src.library) ?? []
      list.push(def)
      map.set(src.library, list)
    }
    return map
  })

  return { all, byKind, bySource, byLibrary }
}

// =============================================================
// DTO ↔ Definition mapping (custom widgets from server)
// =============================================================

/**
 * Map the server's enum-name engine to the SPA's lowercase string union.
 */
function engineFromWire(wire: WidgetEngineWire): WidgetEngine {
  switch (wire) {
    case 'Preset': return 'preset'
    case 'Spec': return 'spec'
    case 'Composite': return 'composite'
  }
}

function engineToWire(engine: WidgetEngine): WidgetEngineWire {
  switch (engine) {
    case 'preset': return 'Preset'
    case 'spec': return 'Spec'
    case 'composite': return 'Composite'
  }
}

export { engineFromWire, engineToWire }

/**
 * Convert a server DTO (custom widget) into the catalog's `WidgetDefinition`
 * shape. Source is always `custom` here.
 */
export function dtoToDefinition(dto: WidgetDefinitionDto): WidgetDefinition {
  const engine = engineFromWire(dto.engine)
  return {
    kind: `custom:${dto.id}`,
    source: 'custom',
    name: dto.name,
    description: dto.description ?? undefined,
    icon: dto.icon,
    engine,
    defaultSize: { w: dto.defaultW, h: dto.defaultH },
    baseKind: (dto.baseKind ?? undefined) as BuiltinKind | undefined,
    // Server stores config as opaque JSON object — the SPA owns the per-kind
    // shape, so the cast through `unknown` is intentional.
    defaultConfig: dto.config as unknown as WidgetConfig,
    spec: dto.spec ?? undefined,
    rowVersion: dto.rowVersion
  }
}

// =============================================================
// Singleton catalog accessor (Nuxt useState)
// =============================================================

/**
 * Single shared catalog instance per client. Backed by Nuxt's `useState` so
 * the underlying refs survive HMR and are SSR-safe (we run SPA-only, but
 * the contract still holds). Call `refreshCustom()` after a save to push
 * fresh definitions into the catalog without remounting consumers.
 *
 * Library refresh is a no-op until iter 3 — kept on the API to avoid a
 * breaking change later.
 */
export interface DashboardWidgetCatalog extends WidgetCatalog {
  /** Reload custom definitions from the server and update the catalog. */
  refreshCustom: () => Promise<void>
  /** Reload library definitions from the server (iter 3+). */
  refreshLibraries: () => Promise<void>
  /** Quick check: is the catalog hydrated? Useful for skeleton states. */
  hydrated: ComputedRef<boolean>
}

export function useWidgetCatalog(): DashboardWidgetCatalog {
  const customDefs = useState<WidgetDefinition[]>('widget-catalog:custom', () => [])
  const libraryDefs = useState<WidgetDefinition[]>('widget-catalog:library', () => [])
  const hydratedFlag = useState<boolean>('widget-catalog:hydrated', () => false)

  const catalog = buildWidgetCatalog(customDefs, libraryDefs)

  async function refreshCustom() {
    const { $widgetService } = useNuxtApp()
    const list = await $widgetService.listCustom()
    customDefs.value = list.map(dtoToDefinition)
    hydratedFlag.value = true
  }

  async function refreshLibraries() {
    // Wired in iter 3 (filesystem) and iter 4 (git install).
    libraryDefs.value = []
  }

  const hydrated = computed(() => hydratedFlag.value)

  return {
    ...catalog,
    refreshCustom,
    refreshLibraries,
    hydrated
  }
}

export type { WidgetSource }
