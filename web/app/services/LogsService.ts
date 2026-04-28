import type { HttpClientService } from './HttpClientService'
import type { LogRecordDto, PageQuery, PagedResponse, TimeWindow } from './types'

/**
 * Reads logs from the Query API. Pure wrapper around the HTTP client — no
 * caching, no state, no retries. Page state belongs to the caller.
 */
export class LogsService {
  constructor(private readonly http: HttpClientService) {}

  listLogs(query: PageQuery): Promise<PagedResponse<LogRecordDto>> {
    return this.http.get<PagedResponse<LogRecordDto>>('/v1/logs', {
      from: query.from,
      to: query.to,
      limit: query.limit,
      cursor: query.cursor,
      traceId: query.traceId,
      service: query.service
    })
  }

  /** Distinct, alphabetically-sorted `service.name` values seen in the window. */
  listServices(window: TimeWindow): Promise<string[]> {
    return this.http.get<string[]>('/v1/logs/services', {
      from: window.from,
      to: window.to
    })
  }
}
