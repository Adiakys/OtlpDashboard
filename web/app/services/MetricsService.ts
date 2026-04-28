import type { HttpClientService } from './HttpClientService'
import type { InstrumentDto, MetricPointsQuery, MetricSeriesDto } from './types'

/**
 * Reads metrics from the Query API. `listInstruments` enumerates all known
 * time-series (bounded by the server's MaxInstruments config — no paging
 * needed); `getPoints` returns the ring-buffer snapshot for one instrument,
 * optionally filtered to a time window.
 */
export class MetricsService {
  constructor(private readonly http: HttpClientService) {}

  listInstruments(): Promise<InstrumentDto[]> {
    return this.http.get<InstrumentDto[]>('/v1/metrics')
  }

  getPoints(query: MetricPointsQuery): Promise<MetricSeriesDto> {
    return this.http.get<MetricSeriesDto>('/v1/metrics/points', {
      resourceHash: query.resourceHash,
      scopeName: query.scopeName,
      instrumentName: query.instrumentName,
      kind: query.kind,
      from: query.from,
      to: query.to
    })
  }

  /** Distinct `service.name` values across currently-recorded instruments. */
  listServices(): Promise<string[]> {
    return this.http.get<string[]>('/v1/metrics/services')
  }
}
