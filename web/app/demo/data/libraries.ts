import type {
  LibraryWidgetDto,
  PackDto,
  WidgetEngine,
  WidgetLibraryDto
} from '~/services/types'
import {
  DEMO_BUNDLE,
  type RawBundledLibrary,
  type RawBundledWidget
} from './bundle'

const DEMO_PACK_ID = 'default'

function engineToWire(engine: string): WidgetEngine {
  switch (engine.toLowerCase()) {
    case 'preset': return 'Preset'
    case 'spec': return 'Spec'
    case 'composite': return 'Composite'
    default: return 'Spec'
  }
}

function widgetFromBundle(raw: RawBundledWidget): LibraryWidgetDto {
  return {
    kindId: raw.slug,
    name: raw.name,
    description: raw.description ?? null,
    icon: raw.icon,
    engine: engineToWire(raw.engine),
    baseKind: raw.baseKind ?? null,
    config: raw.config ?? null,
    spec: raw.spec ?? null,
    parameters: (raw.parameters ?? null) as unknown[] | null,
    defaultW: raw.defaultSize.w,
    defaultH: raw.defaultSize.h
  }
}

function libraryFromBundle(raw: RawBundledLibrary): WidgetLibraryDto {
  return {
    id: raw.id,
    name: raw.name,
    description: raw.description ?? null,
    icon: raw.icon ?? null,
    packId: raw.packId ?? DEMO_PACK_ID,
    widgets: raw.widgets.map(widgetFromBundle)
  }
}

/**
 * Static snapshot of every widget library the demo exposes through
 * `/v1/widgets/libraries`. Pack install/uninstall in demo mode is a
 * no-op (returns 400) — this list is the entire universe.
 */
export const DEMO_LIBRARIES: WidgetLibraryDto[] =
  DEMO_BUNDLE.libraries.map(libraryFromBundle)

/**
 * Static snapshot of the synthetic pack the demo surfaces through
 * `/v1/packs`. The demo is bundled as a single pack containing every
 * library; install/update/uninstall are no-ops. The `removable: false`
 * + `installSource: 'Filesystem'` flags hide the management buttons in
 * the picker — there's nothing meaningful to act on in a static demo.
 */
export const DEMO_PACKS: PackDto[] = [
  {
    id: DEMO_PACK_ID,
    name: 'OpenTelemetry Dashboard — demo pack',
    version: 'demo',
    author: 'OpenTelemetryDashboard',
    license: 'MIT',
    description: 'Bundled demo pack: every widget library plus the starter dashboard.',
    homepage: 'https://github.com/Adiakys/OtlpDashboard',
    installSource: 'Filesystem',
    gitUrl: null,
    gitRef: null,
    gitRefResolved: null,
    gitSubPath: null,
    installedAt: null,
    removable: false,
    libraries: DEMO_LIBRARIES,
    dashboards: DEMO_BUNDLE.dashboards.map(d => ({ id: d.name, builtin: true })),
    // Icons in the bundle were emitted with their original `packId`
    // (e.g. "default") because the sync script preserves provenance.
    // The synthetic demo pack collapses every library/dashboard into a
    // single `demo` pack, so we have to re-stamp icons under the same
    // id and keep the imageUrl untouched (the file already lives at
    // /icons/<originalPackId>/...).
    icons: DEMO_BUNDLE.icons.map(icon => ({
      id: icon.id,
      name: icon.name,
      imageUrl: icon.imageUrl,
      match: icon.match
    }))
  }
]
