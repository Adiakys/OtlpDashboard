import type { HttpClientService } from './HttpClientService'
import type { DashboardDto, SaveDashboardRequest } from './types'

/**
 * CRUD client for the dashboards Query API. The server seeds a "default"
 * dashboard on first migration (see `DEFAULT_DASHBOARD_ID`) so the SPA can
 * land on it without an empty-state branch. `update` carries `rowVersion`
 * for optimistic concurrency — pass back the value most recently returned
 * by the server to avoid a 409.
 */
export class DashboardService {
  constructor(private readonly http: HttpClientService) {}

  list(): Promise<DashboardDto[]> {
    return this.http.get<DashboardDto[]>('/v1/dashboards')
  }

  getById(id: string): Promise<DashboardDto> {
    return this.http.get<DashboardDto>(`/v1/dashboards/${encodeURIComponent(id)}`)
  }

  create(request: SaveDashboardRequest): Promise<DashboardDto> {
    return this.http.post<DashboardDto>('/v1/dashboards', request)
  }

  update(id: string, request: SaveDashboardRequest): Promise<DashboardDto> {
    return this.http.put<DashboardDto>(`/v1/dashboards/${encodeURIComponent(id)}`, request)
  }

  delete(id: string): Promise<void> {
    return this.http.delete<void>(`/v1/dashboards/${encodeURIComponent(id)}`)
  }
}
