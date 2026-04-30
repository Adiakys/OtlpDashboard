import type { HttpClientService } from './HttpClientService'
import type { InstrumentDto, MetricPointsQuery, MetricSeriesDto } from './types'

/**
 * Reads metrics from the Query API. `listInstruments` enumerates all known
 * time-series (bounded by the server's retention policy — no paging needed);
 * `getPoints` returns the recorded points for one instrument, optionally
 * filtered to a time window.
 */
export class MetricsService {
  constructor(private readonly http: HttpClientService) {}

  listInstruments(): Promise<InstrumentDto[]> {
    return this.http.get<InstrumentDto[]>('/v1/metrics')
  }

  getPoints(query: MetricPointsQuery): Promise<MetricSeriesDto> {
    // `includeAttributes` is omitted from the wire when false (the server
    // default) so URLs stay short for the common case.
    const params: Record<string, string | number | boolean | undefined> = {
      resourceHash: query.resourceHash,
      scopeName: query.scopeName,
      instrumentName: query.instrumentName,
      kind: query.kind,
      from: query.from,
      to: query.to
    }
    if (query.includeAttributes) params.includeAttributes = true
    return this.http.get<MetricSeriesDto>('/v1/metrics/points', params)
  }

  /** Distinct `service.name` values across currently-recorded instruments. */
  listServices(): Promise<string[]> {
    return this.http.get<string[]>('/v1/metrics/services')
  }
}
