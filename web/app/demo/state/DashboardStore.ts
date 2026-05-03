import type {
  DashboardDto,
  SaveDashboardRequest
} from '~/services/types'
import type { StorageService } from '../storage/StorageService'
import { SEED_DASHBOARDS } from '../data/dashboards'

const KEY = 'dashboards'

/**
 * Mutable dashboard store backing `/v1/dashboards*` in demo mode. Seeds
 * itself from `SEED_DASHBOARDS` (the bundle's read-only snapshot) on
 * first access; mutations live entirely behind the `StorageService`
 * (in-memory by default, swap in a localStorage impl to persist).
 *
 * Optimistic concurrency is honoured: a mismatched `rowVersion` on
 * `update` throws — same shape as the real server.
 */
export class DashboardStore {
  constructor(private readonly storage: StorageService) {
    if (!this.storage.get<DashboardDto[]>(KEY)) {
      this.storage.set(KEY, clone(SEED_DASHBOARDS))
    }
  }

  list(): DashboardDto[] {
    return this.storage.get<DashboardDto[]>(KEY) ?? []
  }

  getById(id: string): DashboardDto {
    const found = this.list().find((d) => d.id === id)
    if (!found) throw notFound(`Dashboard ${id} not found`)
    return found
  }

  create(req: SaveDashboardRequest): DashboardDto {
    const next: DashboardDto = {
      id: randomUuid(),
      name: req.name,
      widgets: req.widgets,
      updatedAt: new Date().toISOString(),
      rowVersion: 1
    }
    this.storage.set(KEY, [...this.list(), next])
    return next
  }

  update(id: string, req: SaveDashboardRequest): DashboardDto {
    const list = this.list()
    const idx = list.findIndex((d) => d.id === id)
    if (idx < 0) throw notFound(`Dashboard ${id} not found`)
    const existing = list[idx]!
    if (existing.rowVersion !== req.rowVersion) {
      throw conflict('Dashboard was modified by another client; reload and retry')
    }
    const updated: DashboardDto = {
      ...existing,
      name: req.name,
      widgets: req.widgets,
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
    if (!list.some((d) => d.id === id)) throw notFound(`Dashboard ${id} not found`)
    this.storage.set(
      KEY,
      list.filter((d) => d.id !== id)
    )
  }
}

function clone<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T
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

function notFound(message: string): Error {
  return demoError(404, message)
}

function conflict(message: string): Error {
  return demoError(409, message)
}

export function demoError(status: number, message: string): Error & {
  status: number
  response: { status: number; _data: { message: string } }
} {
  const err = Object.assign(new Error(message), {
    status,
    response: { status, _data: { message } }
  })
  return err
}
