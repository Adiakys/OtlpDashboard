export type SeverityBucket = 'trace' | 'debug' | 'info' | 'warn' | 'error' | 'fatal'

export const SEVERITY_BUCKETS: SeverityBucket[] = ['trace', 'debug', 'info', 'warn', 'error', 'fatal']

/** Map an OTLP severity number (1..24) to a logical bucket. */
export function severityBucketFromNumber(n: number): SeverityBucket {
  if (n >= 21) return 'fatal'
  if (n >= 17) return 'error'
  if (n >= 13) return 'warn'
  if (n >= 9) return 'info'
  if (n >= 5) return 'debug'
  return 'trace'
}

export type TraceStatusFilter = 'any' | 'ok' | 'error'

export interface DurationRange {
  /** Inclusive lower bound in milliseconds. `null` means unbounded. */
  minMs: number | null
  /** Inclusive upper bound in milliseconds. `null` means unbounded. */
  maxMs: number | null
}
