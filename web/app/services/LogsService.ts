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
      services: query.services && query.services.length > 0 ? query.services.join(',') : undefined,
      minSeverity: query.minSeverity,
      // Comma-separated list — the server accepts both repeated keys and a
      // single comma-joined string. The single string keeps the URL short
      // when several buckets are selected.
      severities: query.severities && query.severities.length > 0 ? query.severities.join(',') : undefined,
      bodyContains: query.bodyContains,
      attr: query.attr && query.attr.length > 0 ? query.attr : undefined
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
