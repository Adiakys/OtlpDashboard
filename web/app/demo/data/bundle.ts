// Generated artifact: produced by `pnpm sync-demo-fixtures` (a wrapper
// around `web/scripts/sync-demo-fixtures.mjs`). Pulls demo dashboards and
// widget libraries off disk into a single JSON the demo module can import
// without filesystem access at runtime.
//
// Imported only from the demo module, which itself is dynamically loaded
// from a `import.meta.env.VITE_DEMO_MODE` branch — the prod build dead-
// code-eliminates the entire subtree, so this JSON never ships in
// non-demo bundles.

import bundleData from './_bundled.json'

export interface RawBundledWidget {
  slug: string
  name: string
  description?: string
  icon: string
  defaultSize: { w: number; h: number }
  engine: string
  baseKind?: string | null
  config?: Record<string, unknown> | null
  spec?: Record<string, unknown> | null
  parameters?: unknown[] | null
}

export interface RawBundledLibrary {
  id: string
  name: string
  version: string
  author?: string | null
  license?: string | null
  description?: string | null
  widgets: RawBundledWidget[]
}

export interface RawBundledDashboardWidget {
  id: string
  kind: string
  x: number
  y: number
  w: number
  h: number
  config: Record<string, unknown>
}

export interface RawBundledDashboard {
  version: number
  id: string
  name: string
  widgets: RawBundledDashboardWidget[]
}

export interface DemoBundle {
  dashboards: RawBundledDashboard[]
  libraries: RawBundledLibrary[]
}

export const DEMO_BUNDLE: DemoBundle = bundleData as DemoBundle
