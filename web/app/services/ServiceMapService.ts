import type { HttpClientService } from './HttpClientService'
import type { ServiceMapDto, TimeWindow } from './types'

export interface ServiceMapQuery extends TimeWindow {
  /** Optional focus mode: narrows the result to this service and its
   *  direct neighbours. Null/undefined returns the full graph. */
  service?: string | null
}

/**
 * Reads the service-map view from the Query API. Lives at the same
 * level as the metrics / logs / traces services — that the backend
 * derives the graph from spans is an implementation detail of the
 * server-side reader, not part of this contract.
 */
export class ServiceMapService {
  constructor(private readonly http: HttpClientService) {}

  /** Distinct services touched in the window plus the cross-service
   *  call edges between them. Self-loops are filtered server-side.
   *  `service` (optional) narrows the result to that service and its
   *  direct neighbours. */
  getServiceMap(query: ServiceMapQuery): Promise<ServiceMapDto> {
    return this.http.get<ServiceMapDto>('/v1/service-map', {
      from: query.from,
      to: query.to,
      service: query.service ?? undefined
    })
  }
}
