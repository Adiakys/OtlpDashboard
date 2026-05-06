import type { HttpClientService } from './HttpClientService'
import type {
  PageQuery,
  PagedResponse,
  TimeWindow,
  TraceAggregationMetric,
  TraceAggregationsResponse,
  TraceDetailDto,
  TraceSummaryDto
} from './types'

export interface TraceAggregationQuery extends TimeWindow {
  metric: TraceAggregationMetric
  limit?: number
  service?: string | null
  attr?: string[]
}

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

  /** Top-N aggregation grouped by root span name. The server sorts by
   *  the requested `metric`; all four metric columns are returned
   *  regardless so the caller can re-sort client-side without a
   *  refetch. */
  aggregate(query: TraceAggregationQuery): Promise<TraceAggregationsResponse> {
    return this.http.get<TraceAggregationsResponse>('/v1/traces/aggregations', {
      from: query.from,
      to: query.to,
      metric: query.metric,
      limit: query.limit,
      service: query.service ?? undefined,
      attr: query.attr && query.attr.length > 0 ? query.attr : undefined
    })
  }

  /** Distinct, alphabetically-sorted `service.name` values touched by traces in the window. */
  listServices(window: TimeWindow): Promise<string[]> {
    return this.http.get<string[]>('/v1/traces/services', {
      from: window.from,
      to: window.to
    })
  }
}
