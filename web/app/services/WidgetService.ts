import type { HttpClientService } from './HttpClientService'
import type {
  PackDto,
  SaveWidgetDefinitionRequest,
  WidgetDefinitionDto,
  WidgetLibraryDto
} from './types'

/**
 * Single client for the widget surface: custom definitions stored in
 * the DB, the read-only library catalog the picker consumes, and the
 * pack management endpoints that own install / update / uninstall.
 *
 * `update` carries `rowVersion` for optimistic concurrency on custom
 * widgets — pass back the value most recently returned by the server
 * to avoid a 409.
 */
export class WidgetService {
  constructor(private readonly http: HttpClientService) {}

  // ---------- Custom definitions (DB-backed) ----------

  listCustom(): Promise<WidgetDefinitionDto[]> {
    return this.http.get<WidgetDefinitionDto[]>('/v1/widgets/definitions')
  }

  getCustom(id: string): Promise<WidgetDefinitionDto> {
    return this.http.get<WidgetDefinitionDto>(
      `/v1/widgets/definitions/${encodeURIComponent(id)}`
    )
  }

  createCustom(request: SaveWidgetDefinitionRequest): Promise<WidgetDefinitionDto> {
    return this.http.post<WidgetDefinitionDto>('/v1/widgets/definitions', request)
  }

  updateCustom(
    id: string,
    request: SaveWidgetDefinitionRequest
  ): Promise<WidgetDefinitionDto> {
    return this.http.put<WidgetDefinitionDto>(
      `/v1/widgets/definitions/${encodeURIComponent(id)}`,
      request
    )
  }

  deleteCustom(id: string): Promise<void> {
    return this.http.delete<void>(
      `/v1/widgets/definitions/${encodeURIComponent(id)}`
    )
  }

  // ---------- Picker contract: flat library list ----------

  listLibraries(): Promise<WidgetLibraryDto[]> {
    return this.http.get<WidgetLibraryDto[]>('/v1/widgets/libraries')
  }

  // ---------- Pack management ----------

  listPacks(): Promise<PackDto[]> {
    return this.http.get<PackDto[]>('/v1/packs')
  }

  /** Re-scan the packs paths on the server. Picks up new directories
   *  dropped under any configured path without restarting the host. */
  reloadPacks(): Promise<void> {
    return this.http.post<void>('/v1/packs/reload')
  }

  /** Clone a pack from a git host (allow-listed). The server validates
   *  the URL, runs a shallow clone, parses `pack.json`, resolves HEAD
   *  to a commit SHA, and atomically moves the directory into place.
   *  `path` is optional and re-roots the install on a sub-directory of
   *  the clone — useful for monorepos containing multiple packs. */
  installPack(request: { url: string; ref: string; path?: string }): Promise<PackDto> {
    return this.http.post<PackDto>('/v1/packs/install', request)
  }

  /** Re-pull a previously git-installed pack and reset its working
   *  tree to the original ref. Returns 400 if the pack wasn't installed
   *  via git in the first place. */
  updatePack(packId: string): Promise<PackDto> {
    return this.http.post<PackDto>(
      `/v1/packs/${encodeURIComponent(packId)}/update`
    )
  }

  /** Permanently remove a pack directory from the runtime-managed
   *  path. The server returns 400 if the pack lives in a baked-in path
   *  (image layer) — those can only be removed by rebuilding the image. */
  uninstallPack(packId: string): Promise<void> {
    return this.http.delete<void>(
      `/v1/packs/${encodeURIComponent(packId)}`
    )
  }
}
