import type {
  LibraryWidgetDto,
  WidgetEngine,
  WidgetLibraryDto
} from '~/services/types'
import {
  DEMO_BUNDLE,
  type RawBundledLibrary,
  type RawBundledWidget
} from './bundle'

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
    version: raw.version,
    author: raw.author ?? null,
    license: raw.license ?? null,
    description: raw.description ?? null,
    installSource: 'Filesystem',
    gitUrl: null,
    gitRef: null,
    gitRefResolved: null,
    installedAt: null,
    removable: false,
    widgets: raw.widgets.map(widgetFromBundle)
  }
}

/**
 * Static snapshot of every widget library the demo exposes through
 * `/v1/widgets/libraries`. Library install/uninstall in demo mode is a
 * no-op (returns 400) — this list is the entire universe.
 */
export const DEMO_LIBRARIES: WidgetLibraryDto[] =
  DEMO_BUNDLE.libraries.map(libraryFromBundle)
