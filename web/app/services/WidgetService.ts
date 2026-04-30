import type { HttpClientService } from './HttpClientService'
import type { SaveWidgetDefinitionRequest, WidgetDefinitionDto } from './types'

/**
 * CRUD client for the custom widget definitions API. Library-sourced
 * widgets (filesystem / git installed) flow through a separate endpoint
 * group wired in iter 3+ — kept on the same service for ergonomics.
 *
 * `update` carries `rowVersion` for optimistic concurrency — pass back the
 * value most recently returned by the server to avoid a 409.
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
}
