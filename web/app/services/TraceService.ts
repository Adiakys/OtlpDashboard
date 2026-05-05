import type { HttpClientService } from './HttpClientService'
import type { PageQuery, PagedResponse, TimeWindow, TraceDetailDto, TraceSummaryDto } from './types'

/**
 * Reads traces from the Query API — listing (summaries) and detail (all spans).
 */
export class TraceService {
  constructor(private readonly http: HttpClientService) {}

  listTraces(query: PageQuery): Promise<PagedResponse<TraceSummaryDto>> {
    return this.http.get<PagedResponse<TraceSummaryDto>>('/v1/traces', {
      from: query.from,
      to: query.to,
      limit: query.limit,
      cursor: query.cursor,
      service: query.service,
      status: query.status,
      minMs: query.minMs,
      maxMs: query.maxMs,
      spanNameContains: query.spanNameContains,
      attr: query.attr && query.attr.length > 0 ? query.attr : undefined
    })
  }

  getTrace(traceId: string): Promise<TraceDetailDto> {
    return this.http.get<TraceDetailDto>(`/v1/traces/${traceId}`)
  }

  /** Distinct, alphabetically-sorted `service.name` values touched by traces in the window. */
  listServices(window: TimeWindow): Promise<string[]> {
    return this.http.get<string[]>('/v1/traces/services', {
      from: window.from,
      to: window.to
    })
  }
}
