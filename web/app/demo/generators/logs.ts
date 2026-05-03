import type { LogRecordDto, PagedResponse } from '~/services/types'
import { hashString, mulberry32, pick, pickWeighted } from './prng'
import { recentTraceIds } from './traces'

interface LogTemplate {
  service: string
  scope: string
  severity: 'Debug' | 'Info' | 'Warn' | 'Error'
  body: string
}

const TEMPLATES: LogTemplate[] = [
  { service: 'sample-server', scope: 'Microsoft.AspNetCore.Hosting', severity: 'Info',  body: 'Request finished HTTP/1.1 GET /api/v1/products 200 in {ms}ms' },
  { service: 'sample-server', scope: 'Microsoft.AspNetCore.Hosting', severity: 'Info',  body: 'Request finished HTTP/1.1 POST /api/v1/orders 201 in {ms}ms' },
  { service: 'sample-server', scope: 'Demo.Server.Orders',           severity: 'Info',  body: 'Order accepted (id={ms}, items=3)' },
  { service: 'sample-server', scope: 'Demo.Server.Orders',           severity: 'Warn',  body: 'Inventory low for SKU={ms}, falling back to backorder flow' },
  { service: 'sample-server', scope: 'Demo.Server.Orders',           severity: 'Error', body: 'Failed to charge card token=card_***{ms}: gateway returned 502' },
  { service: 'sample-server', scope: 'Microsoft.EntityFrameworkCore.Database.Command', severity: 'Debug', body: "Executed DbCommand ({ms}ms) [Parameters=[@p0='?'], CommandType='Text']" },

  { service: 'sample-client', scope: 'Demo.Client.Worker',           severity: 'Info',  body: 'Polled queue, dequeued {ms} jobs' },
  { service: 'sample-client', scope: 'Demo.Client.Worker',           severity: 'Warn',  body: 'Retrying job {ms} (attempt 2 of 5) — connection reset' },
  { service: 'sample-client', scope: 'Demo.Client.Email',            severity: 'Error', body: 'SMTP send failed for batch={ms}: 421 4.7.0 Try again later' },

  { service: 'postgresql',    scope: 'postgres',                     severity: 'Info',  body: 'duration: {ms} ms  statement: SELECT id, name FROM products WHERE …' },
  { service: 'postgresql',    scope: 'postgres',                     severity: 'Warn',  body: 'autovacuum: VACUUM public.orders (took {ms} ms)' },
  { service: 'postgresql',    scope: 'postgres',                     severity: 'Error', body: 'deadlock detected. Process {ms} waits for ShareLock on transaction…' },

  { service: 'redis',         scope: 'redis-server',                 severity: 'Info',  body: '{ms} clients connected, used_memory_human=12.4M' },
  { service: 'redis',         scope: 'redis-server',                 severity: 'Warn',  body: 'Possible RDB save lag — last save {ms}s ago' }
]

const SEVERITY_NUMBER: Record<LogTemplate['severity'], number> = {
  Debug: 5,
  Info: 9,
  Warn: 13,
  Error: 17
}

/** Same heavy-tailed mix of severities a real app emits. */
const SEVERITY_WEIGHTS = [
  { value: 'Info' as const,  weight: 70 },
  { value: 'Warn' as const,  weight: 20 },
  { value: 'Error' as const, weight: 7 },
  { value: 'Debug' as const, weight: 3 }
]

/**
 * Generate a paged log stream.
 *
 * Logs are dense (one per ~700 ms by default) so the live tail looks
 * busy; the limit caps it so the SPA paints fast. ~5% are correlated
 * with a trace id from the same window so click-through demos work.
 */
export function generateLogList(args: {
  fromMs: number
  toMs: number
  limit: number
  service?: string | null
  minSeverity?: number
  traceIdFilter?: string | null
}): PagedResponse<LogRecordDto> {
  const seed = hashString(
    `logs|${args.fromMs}|${args.toMs}|${args.service ?? ''}|${args.traceIdFilter ?? ''}`
  )
  const rand = mulberry32(seed)
  const traceSeed = hashString(`traces|${args.fromMs}|${args.toMs}|${args.service ?? ''}`)
  const traceIds = recentTraceIds(traceSeed, 32)

  const candidates = args.service
    ? TEMPLATES.filter((t) => t.service === args.service)
    : TEMPLATES
  if (candidates.length === 0) return { items: [], nextCursor: null }

  const windowMs = args.toMs - args.fromMs
  const desiredCount = Math.min(args.limit, Math.max(20, Math.floor(windowMs / 700)))
  const items: LogRecordDto[] = []

  for (let i = 0; i < desiredCount; i++) {
    const targetSeverity = pickWeighted(rand, SEVERITY_WEIGHTS)
    const matching = candidates.filter((t) => t.severity === targetSeverity)
    const tpl = matching.length > 0 ? pick(rand, matching) : pick(rand, candidates)
    const sevNum = SEVERITY_NUMBER[tpl.severity]
    if (args.minSeverity != null && sevNum < args.minSeverity) {
      // Skip emit but tick `i` still so density doesn't collapse to bursts.
      continue
    }

    const t = args.fromMs + Math.floor(rand() * windowMs)
    const correlatedTrace = rand() < 0.05 ? pick(rand, traceIds) : null
    const traceId = args.traceIdFilter ?? correlatedTrace
    if (args.traceIdFilter && traceId !== args.traceIdFilter) continue

    const numericFiller = Math.floor(10 + rand() * 9990)
    items.push({
      time: new Date(t).toISOString(),
      observedTime: new Date(t).toISOString(),
      severityNumber: sevNum,
      severityText: tpl.severity,
      body: tpl.body.replace('{ms}', String(numericFiller)),
      traceId,
      spanId: traceId ? traceId.slice(0, 16) : null,
      scopeName: tpl.scope,
      scopeVersion: '1.0.0',
      resourceHash: `demo-${tpl.service}`,
      serviceName: tpl.service,
      attributes: {}
    })
  }

  items.sort((a, b) => b.time.localeCompare(a.time))
  return { items: items.slice(0, args.limit), nextCursor: null }
}
