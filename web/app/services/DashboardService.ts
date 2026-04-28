import type { HttpClientService } from './HttpClientService'
import type { DashboardDto, SaveDashboardRequest } from './types'

/**
 * Reads and writes the singleton "default" dashboard. The server lazy-creates
 * the row on first GET so callers never see a 404; PUT bumps `rowVersion` and
 * surfaces 409 when a concurrent writer beat us to it.
 */
export class DashboardService {
  constructor(private readonly http: HttpClientService) {}

  getDefault(): Promise<DashboardDto> {
    return this.http.get<DashboardDto>('/v1/dashboards/default')
  }

  saveDefault(request: SaveDashboardRequest): Promise<DashboardDto> {
    return this.http.put<DashboardDto>('/v1/dashboards/default', request)
  }
}
