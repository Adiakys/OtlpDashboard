import { computed, defineAsyncComponent, ref, type Component, type ComputedRef, type Ref } from 'vue'
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
import type { ParameterDecl } from '~/lib/htmlEngine/types'
import type {
  WidgetDefinitionDto,
  WidgetEngine as WidgetEngineWire,
  WidgetLibraryDto
} from '~/services/types'

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

// Lazy host + form for `engine: 'spec'` widgets. The `spec` engine slot
// is repurposed to host the HTML template engine — DOMPurify (~50 KB
// gzip) is loaded only when at least one html-engine widget is mounted.
const HtmlWidget = defineAsyncComponent(() => import('./widgets/HtmlWidget.vue'))
const HtmlConfigForm = defineAsyncComponent(() => import('./configs/HtmlConfigForm.vue'))

/**
 * Turn a `WidgetDefinition` into the Vue component to mount in the grid.
 * Engine dispatch:
 *   - `preset`    → render the wrapped builtin's component.
 *   - `spec`      → render the HTML template engine host.
 *   - `composite` → render the composite host (iter 5 — placeholder).
 */
export function resolveComponent(def: WidgetDefinition | null): Component | null {
  if (def === null) return null
  if (def.engine === 'preset' && def.baseKind) {
    return WIDGET_REGISTRY[def.baseKind].component
  }
  if (def.engine === 'spec') {
    return HtmlWidget
  }
  // Composite engine wired in iter 5.
  return null
}

/**
 * Turn a `WidgetDefinition` into the Vue component to mount in the config
 * drawer. For `preset`, the builtin's own form drives the per-instance
 * config; for `spec`, the HTML form edits per-instance bindings (custom
 * widgets gain a template editor in iter 2b).
 */
export function resolveConfigForm(def: WidgetDefinition | null): Component | null {
  if (def === null) return null
  if (def.engine === 'preset' && def.baseKind) {
    return WIDGET_REGISTRY[def.baseKind].configForm
  }
  if (def.engine === 'spec') {
    return HtmlConfigForm
  }
  return null
}

/**
 * The default config the picker copies into a fresh widget instance of the
 * given definition. For `preset`, this is the definition's `defaultConfig`
 * (a clone, so each instance starts with its own object).
 *
 * When the definition declares `parameters[]`, the seed also gets a
 * `parameters` map populated from each declaration's `default`. The
 * widget's metric binding(s) keep the `${param}` placeholders from the
 * definition; they're substituted by the runtime expansion path in the
 * widget at query time. Parameter changes therefore propagate to every
 * binding without the form having to rewrite the metric on every keystroke.
 */
export function defaultConfigForDefinition(def: WidgetDefinition): WidgetConfig {
  // JSON round-trip is safe for our config shapes (no Date / Map /
  // undefined-as-significant). Two instances added back-to-back must not
  // share the same nested object reference.
  const seedRaw = def.engine === 'spec' && !def.defaultConfig
    ? { bindings: {}, range: 'last-1h' }
    : (def.defaultConfig ?? {})
  const seed = JSON.parse(JSON.stringify(seedRaw)) as Record<string, unknown>

  if (def.parameters?.length) {
    seed.parameters = parameterSeed(def.parameters)
  }

  return seed as WidgetConfig
}

function parameterSeed(decls: ParameterDecl[]): Record<string, string | number | boolean> {
  const out: Record<string, string | number | boolean> = {}
  for (const d of decls) {
    // `default` is optional; when missing the widget renders without a
    // value and the form's required-flag blocks Apply.
    if (d.type === 'number' && typeof d.default === 'number') out[d.name] = d.default
    else if (d.type === 'boolean' && typeof d.default === 'boolean') out[d.name] = d.default
    else if (typeof d.default === 'string' && d.default !== '') out[d.name] = d.default
  }
  return out
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
 * Flatten a library DTO (filesystem-discovered pack) into a list of
 * `WidgetDefinition`s, one per widget. The fully-qualified kind takes the
 * shape `library:<libraryId>/<kindId>` so the resolver can disambiguate
 * between libraries that happen to expose the same kind id.
 */
export function libraryDtoToDefinitions(lib: WidgetLibraryDto): WidgetDefinition[] {
  const out: WidgetDefinition[] = []
  for (const w of lib.widgets) {
    // Server stays opaque on the parameter schema; the SPA owns it.
    // Cast through `unknown` so typescript doesn't try to validate the
    // ParameterDecl union shape from the wire — invalid declarations
    // simply get silently ignored by the parameter UI.
    const parameters = (w.parameters ?? undefined) as ParameterDecl[] | undefined
    out.push({
      kind: `library:${lib.id}/${w.kindId}`,
      source: { library: lib.id },
      name: w.name,
      description: w.description ?? undefined,
      icon: w.icon,
      engine: engineFromWire(w.engine),
      defaultSize: { w: w.defaultW, h: w.defaultH },
      baseKind: (w.baseKind ?? undefined) as BuiltinKind | undefined,
      defaultConfig: (w.config ?? {}) as unknown as WidgetConfig,
      spec: w.spec ?? undefined,
      parameters
    })
  }
  return out
}

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
  /** Look up the source library's metadata (e.g. `removable`) by id.
   *  Returns null when the catalog hasn't seen the library yet. */
  libraryById: (id: string) => WidgetLibraryDto | null
  /** Quick check: is the catalog hydrated? Useful for skeleton states. */
  hydrated: ComputedRef<boolean>
}

export function useWidgetCatalog(): DashboardWidgetCatalog {
  const customDefs = useState<WidgetDefinition[]>('widget-catalog:custom', () => [])
  const libraryDefs = useState<WidgetDefinition[]>('widget-catalog:library', () => [])
  const libraryDtos = useState<WidgetLibraryDto[]>('widget-catalog:libraryDtos', () => [])
  const hydratedFlag = useState<boolean>('widget-catalog:hydrated', () => false)

  const catalog = buildWidgetCatalog(customDefs, libraryDefs)

  async function refreshCustom() {
    const { $widgetService } = useNuxtApp()
    const list = await $widgetService.listCustom()
    customDefs.value = list.map(dtoToDefinition)
    hydratedFlag.value = true
  }

  async function refreshLibraries() {
    const { $widgetService } = useNuxtApp()
    const libs = await $widgetService.listLibraries()
    const flattened: WidgetDefinition[] = []
    for (const lib of libs) flattened.push(...libraryDtoToDefinitions(lib))
    libraryDefs.value = flattened
    libraryDtos.value = libs
  }

  function libraryById(id: string): WidgetLibraryDto | null {
    return libraryDtos.value.find(l => l.id === id) ?? null
  }

  const hydrated = computed(() => hydratedFlag.value)

  return {
    ...catalog,
    refreshCustom,
    refreshLibraries,
    libraryById,
    hydrated
  }
}

export type { WidgetSource }
