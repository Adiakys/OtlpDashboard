import type { HttpClientService } from './HttpClientService'
import type { DashboardInfoDto } from './types'

/**
 * Reads dashboard presentation metadata from the public
 * <c>GET /api/v1/info</c> endpoint (no auth required). Used at app boot to
 * surface the configured <c>ApplicationName</c> in the sidebar and login
 * form — the SPA is statically compiled and can't read server env vars
 * directly.
 */
export class InfoService {
  constructor(private readonly http: HttpClientService) {}

  getInfo(): Promise<DashboardInfoDto> {
    return this.http.get<DashboardInfoDto>('/v1/info')
  }
}
