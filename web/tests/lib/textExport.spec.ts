import { describe, expect, it } from 'vitest'
import { buildLogfmt, buildTraceTree, buildTraceTrees } from '~/lib/textExport'
import type { LogRecordDto, SpanDto } from '~/services/types'

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
