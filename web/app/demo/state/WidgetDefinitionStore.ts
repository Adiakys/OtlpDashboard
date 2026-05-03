import type {
  SaveWidgetDefinitionRequest,
  WidgetDefinitionDto
} from '~/services/types'
import type { StorageService } from '../storage/StorageService'
import { demoError } from './DashboardStore'

const KEY = 'widget-definitions'

/**
 * In-demo store for custom (DB-backed-on-server) widget definitions.
 * The demo seeds nothing — the user creates their own. Storage
 * abstraction lets us swap in localStorage persistence later without
 * changing call sites.
 */
export class WidgetDefinitionStore {
  constructor(private readonly storage: StorageService) {
    if (!this.storage.get<WidgetDefinitionDto[]>(KEY)) {
      this.storage.set<WidgetDefinitionDto[]>(KEY, [])
    }
  }

  list(): WidgetDefinitionDto[] {
    return this.storage.get<WidgetDefinitionDto[]>(KEY) ?? []
  }

  getById(id: string): WidgetDefinitionDto {
    const found = this.list().find((w) => w.id === id)
    if (!found) throw demoError(404, `Widget definition ${id} not found`)
    return found
  }

  create(req: SaveWidgetDefinitionRequest): WidgetDefinitionDto {
    const next: WidgetDefinitionDto = {
      id: randomUuid(),
      name: req.name,
      description: req.description,
      icon: req.icon,
      engine: req.engine,
      baseKind: req.baseKind,
      config: req.config,
      spec: req.spec,
      defaultW: req.defaultW,
      defaultH: req.defaultH,
      updatedAt: new Date().toISOString(),
      rowVersion: 1
    }
    this.storage.set(KEY, [...this.list(), next])
    return next
  }

  update(id: string, req: SaveWidgetDefinitionRequest): WidgetDefinitionDto {
    const list = this.list()
    const idx = list.findIndex((w) => w.id === id)
    if (idx < 0) throw demoError(404, `Widget definition ${id} not found`)
    const existing = list[idx]!
    if (existing.rowVersion !== req.rowVersion) {
      throw demoError(409, 'Widget definition was modified by another client')
    }
    const updated: WidgetDefinitionDto = {
      ...existing,
      name: req.name,
      description: req.description,
      icon: req.icon,
      engine: req.engine,
      baseKind: req.baseKind,
      config: req.config,
      spec: req.spec,
      defaultW: req.defaultW,
      defaultH: req.defaultH,
      updatedAt: new Date().toISOString(),
      rowVersion: existing.rowVersion + 1
    }
    const nextList = [...list]
    nextList[idx] = updated
    this.storage.set(KEY, nextList)
    return updated
  }

  delete(id: string): void {
    const list = this.list()
    if (!list.some((w) => w.id === id)) throw demoError(404, `Widget definition ${id} not found`)
    this.storage.set(KEY, list.filter((w) => w.id !== id))
  }
}

function randomUuid(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16)
  })
}
