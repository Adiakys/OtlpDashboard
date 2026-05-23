import { describe, expect, it } from 'vitest'
import {
  buildLogfmt,
  buildLogsCsv,
  buildTraceTree,
  buildTraceTrees,
  buildTracesCsv
} from '~/lib/textExport'
import type { LogRecordDto, SpanDto, TraceSummaryDto } from '~/services/types'

function log(partial: Partial<LogRecordDto> = {}): LogRecordDto {
  return {
    time: '2026-05-23T12:00:00.000Z',
    observedTime: null,
    severityNumber: 9,
    severityText: 'Info',
    body: 'hello',
    traceId: null,
    spanId: null,
    scopeName: 'app.requests',
    scopeVersion: '1.0.0',
    resourceHash: 'aaaa',
    serviceName: 'svc-a',
    attributes: {},
    ...partial
  }
}

function span(partial: Partial<SpanDto> = {}): SpanDto {
  return {
    spanId: 's1',
    parentSpanId: null,
    name: 'GET /things',
    kind: 'Server',
    start: '2026-05-23T12:00:00.000Z',
    end: '2026-05-23T12:00:00.150Z',
    durationMs: 150,
    statusCode: 'Ok',
    statusMessage: null,
    scopeName: 'app.requests',
    scopeVersion: '1.0.0',
    serviceName: 'svc-a',
    attributes: {},
    events: [],
    links: [],
    ...partial
  }
}

describe('buildLogfmt', () => {
  it('renders one line per record', () => {
    const out = buildLogfmt([log({ body: 'a' }), log({ body: 'b' })])
    expect(out.trimEnd().split('\n')).toHaveLength(2)
  })

  it('puts time, level, service, scope, msg in the expected order', () => {
    const out = buildLogfmt([log({ body: 'hi', severityText: 'Warn' })]).trim()
    expect(out).toBe(
      'time=2026-05-23T12:00:00.000Z level=Warn service=svc-a scope=app.requests msg=hi'
    )
  })

  it('quotes values containing spaces, equals, quotes, or backslashes', () => {
    const out = buildLogfmt([log({ body: 'connection refused' })]).trim()
    expect(out).toContain('msg="connection refused"')

    const out2 = buildLogfmt([log({ body: 'a"b\\c' })]).trim()
    expect(out2).toContain('msg="a\\"b\\\\c"')
  })

  it('escapes newlines so each record stays on one line', () => {
    const out = buildLogfmt([log({ body: 'first\nsecond' })]).trim()
    expect(out.split('\n')).toHaveLength(1)
    expect(out).toContain('msg="first\\nsecond"')
  })

  it('renders trace_id/span_id correlation when present', () => {
    const out = buildLogfmt([log({
      traceId: 'aabbccdd',
      spanId: '01020304'
    })]).trim()
    expect(out).toContain('trace_id=aabbccdd')
    expect(out).toContain('span_id=01020304')
  })

  it('namespaces attributes under attr.* and drops null/undefined', () => {
    const out = buildLogfmt([log({
      attributes: {
        user_id: 42,
        feature: 'beta',
        empty: null,
        missing: undefined
      }
    })]).trim()
    expect(out).toContain('attr.user_id=42')
    expect(out).toContain('attr.feature=beta')
    expect(out).not.toContain('attr.empty')
    expect(out).not.toContain('attr.missing')
  })

  it('flattens nested attribute values to JSON', () => {
    const out = buildLogfmt([log({
      attributes: { tags: ['a', 'b'], nested: { k: 1 } }
    })]).trim()
    expect(out).toContain('attr.tags="[\\"a\\",\\"b\\"]"')
    expect(out).toContain('attr.nested="{\\"k\\":1}"')
  })

  it('returns an empty string for an empty input', () => {
    expect(buildLogfmt([])).toBe('')
  })

  it('falls back to numeric severity when the text label is absent', () => {
    const out = buildLogfmt([log({ severityText: null, severityNumber: 13 })]).trim()
    expect(out).toContain('level=13')
  })
})

describe('buildTraceTree', () => {
  it('renders a single-span trace with a header line', () => {
    const out = buildTraceTree({ traceId: 't1', spans: [span()] }).trim().split('\n')
    expect(out[0]).toContain('trace=t1')
    expect(out[0]).toContain('spans=1')
    expect(out[1]).toContain('- GET /things')
    expect(out[1]).toContain('[server ok 150.0ms]')
    expect(out[1]).toContain('service=svc-a')
  })

  it('nests children under their parent by two-space indent', () => {
    const out = buildTraceTree({
      traceId: 't1',
      spans: [
        span({ spanId: 'root', name: 'root' }),
        span({ spanId: 'child', parentSpanId: 'root', name: 'child' }),
        span({ spanId: 'grand', parentSpanId: 'child', name: 'grand' })
      ]
    })
    const lines = out.split('\n')
    const rootLine = lines.find(l => l.includes('- root'))!
    const childLine = lines.find(l => l.includes('- child'))!
    const grandLine = lines.find(l => l.includes('- grand'))!
    expect(rootLine.startsWith('- ')).toBe(true)
    expect(childLine.startsWith('  - ')).toBe(true)
    expect(grandLine.startsWith('    - ')).toBe(true)
  })

  it('orders siblings by start time', () => {
    const out = buildTraceTree({
      traceId: 't1',
      spans: [
        span({ spanId: 'p', name: 'p' }),
        span({ spanId: 'b', parentSpanId: 'p', name: 'second', start: '2026-05-23T12:00:00.200Z' }),
        span({ spanId: 'a', parentSpanId: 'p', name: 'first', start: '2026-05-23T12:00:00.100Z' })
      ]
    })
    const lines = out.split('\n').filter(l => l.includes('- '))
    const aIdx = lines.findIndex(l => l.includes('first'))
    const bIdx = lines.findIndex(l => l.includes('second'))
    expect(aIdx).toBeLessThan(bIdx)
  })

  it('promotes orphans (missing parent) to depth 0', () => {
    const out = buildTraceTree({
      traceId: 't1',
      spans: [span({ spanId: 'orphan', parentSpanId: 'missing', name: 'orphan' })]
    })
    expect(out).toContain('\n- orphan')
  })

  it('surfaces status message under attr.error when status is Error', () => {
    const out = buildTraceTree({
      traceId: 't1',
      spans: [span({ statusCode: 'Error', statusMessage: 'boom' })]
    })
    expect(out).toContain('[server error 150.0ms]')
    expect(out).toContain('error=boom')
  })

  it('renders a placeholder for a trace with no spans', () => {
    const out = buildTraceTree({ traceId: 't1', spans: [] })
    expect(out).toContain('spans=0')
    expect(out).toContain('(no spans)')
  })
})

describe('buildTraceTrees', () => {
  it('joins multiple traces with a blank line separator', () => {
    const out = buildTraceTrees([
      { traceId: 't1', spans: [span({ spanId: 's1' })] },
      { traceId: 't2', spans: [span({ spanId: 's2' })] }
    ])
    // Each trace block ends with "\n", and they are joined with "\n",
    // producing a blank line between blocks.
    expect(out).toMatch(/trace=t1[\s\S]+\n\ntrace=t2/)
  })
})

function traceSummary(partial: Partial<TraceSummaryDto> = {}): TraceSummaryDto {
  return {
    traceId: 't1',
    rootSpanName: 'GET /things',
    start: '2026-05-23T12:00:00.000Z',
    end: '2026-05-23T12:00:00.150Z',
    durationMs: 150,
    spanCount: 4,
    rootStatusCode: 'Ok',
    resourceHash: 'aaaa',
    serviceName: 'svc-a',
    otherServiceNames: [],
    ...partial
  }
}

describe('buildLogsCsv', () => {
  it('emits a header row even when there are no records', () => {
    const out = buildLogsCsv([])
    expect(out.trim()).toBe('time,service,severity,scope,trace_id,span_id,body')
  })

  it('renders columns in the on-screen order: time, service, severity, scope, trace_id, span_id, body', () => {
    const out = buildLogsCsv([log({
      body: 'hello',
      severityText: 'Warn',
      traceId: 'tt',
      spanId: 'ss'
    })])
    const lines = out.trim().split('\n')
    expect(lines[0]).toBe('time,service,severity,scope,trace_id,span_id,body')
    expect(lines[1]).toBe('2026-05-23T12:00:00.000Z,svc-a,Warn,app.requests,tt,ss,hello')
  })

  it('quotes fields containing commas, quotes, or newlines (RFC 4180)', () => {
    const out = buildLogsCsv([log({ body: 'a,b' })])
    expect(out).toContain(',"a,b"\n')

    const out2 = buildLogsCsv([log({ body: 'say "hi"' })])
    expect(out2).toContain(',"say ""hi"""\n')

    const out3 = buildLogsCsv([log({ body: 'first\nsecond' })])
    expect(out3).toContain(',"first\nsecond"\n')
  })

  it('leaves empty values blank rather than quoted', () => {
    const out = buildLogsCsv([log({ serviceName: null, scopeName: null, traceId: null, spanId: null, body: null })])
    const row = out.trim().split('\n')[1]!
    expect(row).toBe('2026-05-23T12:00:00.000Z,,Info,,,,')
  })

  it('falls back to severity number when text is absent', () => {
    const out = buildLogsCsv([log({ severityText: null, severityNumber: 13 })])
    expect(out.trim().split('\n')[1]).toContain(',13,')
  })
})

describe('buildTracesCsv', () => {
  it('emits a header row even when there are no records', () => {
    const out = buildTracesCsv([])
    expect(out.trim()).toBe('start,service,root_span,duration_ms,spans,status,trace_id')
  })

  it('renders columns in the on-screen order: start, service, root_span, duration_ms, spans, status, trace_id', () => {
    const out = buildTracesCsv([traceSummary()])
    const lines = out.trim().split('\n')
    expect(lines[0]).toBe('start,service,root_span,duration_ms,spans,status,trace_id')
    expect(lines[1]).toBe('2026-05-23T12:00:00.000Z,svc-a,GET /things,150,4,Ok,t1')
  })

  it('quotes a root span name containing a comma', () => {
    const out = buildTracesCsv([traceSummary({ rootSpanName: 'GET /a, /b' })])
    expect(out).toContain(',"GET /a, /b",')
  })

  it('leaves service blank when null', () => {
    const out = buildTracesCsv([traceSummary({ serviceName: null })])
    const row = out.trim().split('\n')[1]!
    expect(row).toBe('2026-05-23T12:00:00.000Z,,GET /things,150,4,Ok,t1')
  })
})
