#!/usr/bin/env node
// Pre-build step for the GitHub Pages demo bundle.
//
// Walks `demo/packs/*/pack.json` from the repo root and rolls each
// referenced library + dashboard into one deterministic JSON file
// checked into `web/app/demo/data/_bundled.json`. The demo module
// imports the JSON; the real app never loads it (dead-code-eliminated
// when VITE_DEMO_MODE is unset).
//
// Run via `pnpm sync-demo-fixtures` (also chained from
// `pnpm generate:demo`).

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..')
const PACKS_DIR = path.join(REPO_ROOT, 'demo', 'packs')
const OUT_DIR = path.join(__dirname, '..', 'app', 'demo', 'data')
const OUT_FILE = path.join(OUT_DIR, '_bundled.json')

function readJson(p) {
  return JSON.parse(fs.readFileSync(p, 'utf-8'))
}

function listDirs(p) {
  return fs
    .readdirSync(p, { withFileTypes: true })
    .filter((d) => d.isDirectory())
    .map((d) => d.name)
    .sort()
}

function loadLibrary(libRoot) {
  const manifestPath = path.join(libRoot, 'manifest.json')
  if (!fs.existsSync(manifestPath)) return null
  const manifest = readJson(manifestPath)
  const widgets = []
  const widgetsDir = path.join(libRoot, 'widgets')
  if (fs.existsSync(widgetsDir)) {
    for (const slug of listDirs(widgetsDir)) {
      const widgetJson = path.join(widgetsDir, slug, 'widget.json')
      if (fs.existsSync(widgetJson)) {
        widgets.push({ slug, ...readJson(widgetJson) })
      }
    }
  }
  // Whitelist what the bundle exposes — pack-level fields like
  // `version`/`author`/`license` are no longer part of the manifest.
  return {
    id: manifest.id,
    name: manifest.name,
    description: manifest.description ?? null,
    icon: manifest.icon ?? null,
    widgets
  }
}

// Icons are copied to web/public/icons/<packId>/<iconId>/<filename> at
// bundle time and surfaced under the same path the real backend's pack
// asset endpoint would serve them from. Keeping the URL shape close to
// the production one means the SPA's icon resolver doesn't need a
// demo-mode branch.
const PUBLIC_ICONS_DIR = path.join(__dirname, '..', 'public', 'icons')

const bundle = { dashboards: [], libraries: [], icons: [] }
const seenLibIds = new Set()

if (fs.existsSync(PACKS_DIR)) {
  // Wipe the previous demo icon layout — pack additions/removals shouldn't
  // leave stale SVGs lying around between builds.
  if (fs.existsSync(PUBLIC_ICONS_DIR)) {
    fs.rmSync(PUBLIC_ICONS_DIR, { recursive: true, force: true })
  }

  for (const packDir of listDirs(PACKS_DIR)) {
    const packRoot = path.join(PACKS_DIR, packDir)
    const packJsonPath = path.join(packRoot, 'pack.json')
    if (!fs.existsSync(packJsonPath)) continue
    const pack = readJson(packJsonPath)

    for (const libRef of pack.libraries ?? []) {
      const libRoot = path.join(packRoot, libRef.path)
      const lib = loadLibrary(libRoot)
      if (!lib) continue
      if (seenLibIds.has(lib.id)) continue
      seenLibIds.add(lib.id)
      bundle.libraries.push({ ...lib, packId: pack.id })
    }

    for (const dashRef of pack.dashboards ?? []) {
      const dashPath = path.join(packRoot, dashRef.path)
      if (!fs.existsSync(dashPath)) continue
      bundle.dashboards.push(readJson(dashPath))
    }

    for (const iconRef of pack.icons ?? []) {
      const iconRoot = path.join(packRoot, iconRef.path)
      const iconJsonPath = path.join(iconRoot, 'icon.json')
      if (!fs.existsSync(iconJsonPath)) continue
      const desc = readJson(iconJsonPath)
      const srcImage = path.join(iconRoot, desc.image)
      if (!fs.existsSync(srcImage)) continue

      // Mirror the shape of the production asset URL so the SPA
      // resolver builds one path that works in both modes.
      const relUrl = `/icons/${pack.id}/${iconRef.path.split('/').pop()}/${desc.image}`
      const dstPath = path.join(__dirname, '..', 'public', relUrl.slice(1))
      fs.mkdirSync(path.dirname(dstPath), { recursive: true })
      fs.copyFileSync(srcImage, dstPath)

      bundle.icons.push({
        packId: pack.id,
        id: desc.id,
        name: desc.name,
        imageUrl: relUrl,
        match: desc.match
      })
    }
  }
}

fs.mkdirSync(OUT_DIR, { recursive: true })
fs.writeFileSync(OUT_FILE, JSON.stringify(bundle, null, 2) + '\n')

const widgetCount = bundle.libraries.reduce((s, l) => s + l.widgets.length, 0)
console.log(
  `[sync-demo-fixtures] wrote ${path.relative(REPO_ROOT, OUT_FILE)} ` +
    `(${bundle.dashboards.length} dashboards, ${bundle.libraries.length} libraries, ` +
    `${widgetCount} widgets, ${bundle.icons.length} icons)`
)
