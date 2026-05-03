#!/usr/bin/env node
// Pre-build step for the GitHub Pages demo bundle.
//
// Reads `demo/dashboards/*.json` and `demo/widget-libraries/**` from the
// repo root and rolls them into one deterministic JSON file checked into
// `web/app/demo/data/_bundled.json`. The demo module imports the JSON; the
// real app never loads it (dead-code-eliminated when VITE_DEMO_MODE is
// unset).
//
// Run via `pnpm sync-demo-fixtures` (also chained from `pnpm generate:demo`).

import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const REPO_ROOT = path.resolve(__dirname, '..', '..')
const DEMO_DIR = path.join(REPO_ROOT, 'demo')
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

function listJsonFiles(p) {
  return fs
    .readdirSync(p)
    .filter((f) => f.endsWith('.json'))
    .sort()
}

const bundle = { dashboards: [], libraries: [] }

const dashboardsDir = path.join(DEMO_DIR, 'dashboards')
if (fs.existsSync(dashboardsDir)) {
  for (const f of listJsonFiles(dashboardsDir)) {
    bundle.dashboards.push(readJson(path.join(dashboardsDir, f)))
  }
}

const librariesRoot = path.join(DEMO_DIR, 'widget-libraries')
if (fs.existsSync(librariesRoot)) {
  for (const libDirName of listDirs(librariesRoot)) {
    const libPath = path.join(librariesRoot, libDirName)
    const manifestPath = path.join(libPath, 'manifest.json')
    if (!fs.existsSync(manifestPath)) continue
    const manifest = readJson(manifestPath)
    const widgets = []
    const widgetsDir = path.join(libPath, 'widgets')
    if (fs.existsSync(widgetsDir)) {
      for (const slug of listDirs(widgetsDir)) {
        const widgetJson = path.join(widgetsDir, slug, 'widget.json')
        if (fs.existsSync(widgetJson)) {
          widgets.push({ slug, ...readJson(widgetJson) })
        }
      }
    }
    bundle.libraries.push({ ...manifest, widgets })
  }
}

fs.mkdirSync(OUT_DIR, { recursive: true })
fs.writeFileSync(OUT_FILE, JSON.stringify(bundle, null, 2) + '\n')

const widgetCount = bundle.libraries.reduce((s, l) => s + l.widgets.length, 0)
console.log(
  `[sync-demo-fixtures] wrote ${path.relative(REPO_ROOT, OUT_FILE)} ` +
    `(${bundle.dashboards.length} dashboards, ${bundle.libraries.length} libraries, ${widgetCount} widgets)`
)
