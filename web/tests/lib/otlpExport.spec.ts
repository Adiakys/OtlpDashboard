import { describe, expect, it } from 'vitest'
import {
  buildLogsExport,
  buildSpansExport,
  isoToUnixNano
} from '~/lib/otlpExport'
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
    spanId: '0102030405060708',
    parentSpanId: null,
    name: 'GET /things',
    kind: 'Server',
    start: '2026-05-23T12:00:00.000Z',
    end: '2026-05-23T12:00:01.000Z',
    durationMs: 1000,
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

describe('isoToUnixNano', () => {
  it('converts millisecond-precision UTC', () => {
    // 1970-01-01T00:00:00.001Z = 1ms = 1_000_000ns
    expect(isoToUnixNano('1970-01-01T00:00:00.001Z')).toBe('1000000')
  })

  it('preserves sub-millisecond precision (100ns ticks from .NET)', () => {
    // Source: epoch + 1.1234567s = 1_123_456_700ns
    expect(isoToUnixNano('1970-01-01T00:00:01.1234567Z')).toBe('1123456700')
  })

  it('handles ISO without fractional seconds', () => {
    expect(isoToUnixNano('1970-01-01T00:00:02Z')).toBe('2000000000')
  })

  it('treats unsuffixed ISO as UTC', () => {
    expect(isoToUnixNano('1970-01-01T00:00:00.5')).toBe('500000000')
  })

  it('returns 0 for unparseable input', () => {
    expect(isoToUnixNano('not a date')).toBe('0')
  })
})

describe('buildLogsExport', () => {
  it('wraps the result in an OTLP envelope', () => {
    const out = buildLogsExport([log()])
    expect(out).toHaveProperty('resourceLogs')
    expect(Array.isArray(out.resourceLogs)).toBe(true)
  })

  it('groups records by (resourceHash + serviceName) then by scope', () => {
    const out = buildLogsExport([
      log({ resourceHash: 'aaaa', serviceName: 'svc-a', scopeName: 's1' }),
      log({ resourceHash: 'aaaa', serviceName: 'svc-a', scopeName: 's2' }),
      log({ resourceHash: 'bbbb', serviceName: 'svc-b', scopeName: 's1' })
    ])
    expect(out.resourceLogs).toHaveLength(2)
    const a = out.resourceLogs.find(r =>
      r.resource.attributes.some(a => a.key === 'service.name' && a.value.stringValue === 'svc-a'))!
    expect(a.scopeLogs).toHaveLength(2)
    expect(a.scopeLogs.map(s => s.scope.name).sort()).toEqual(['s1', 's2'])
  })

  it('encodes service.name and dashboard.resource_hash as resource attributes', () => {
    const out = buildLogsExport([log({ resourceHash: 'fp123', serviceName: 'svc-x' })])
    const attrs = out.resourceLogs[0]!.resource.attributes
    const byKey = Object.fromEntries(attrs.map(a => [a.key, a.value]))
    expect(byKey['service.name']).toEqual({ stringValue: 'svc-x' })
    expect(byKey['dashboard.resource_hash']).toEqual({ stringValue: 'fp123' })
  })

  it('emits timeUnixNano as a string', () => {
    const out = buildLogsExport([log({ time: '2026-05-23T12:34:56.000Z' })])
    const t = out.resourceLogs[0]!.scopeLogs[0]!.logRecords[0]!.timeUnixNano
    expect(typeof t).toBe('string')
    expect(t).toMatch(/^\d+$/)
  })

  it('keeps the log body under value.stringValue', () => {
    const out = buildLogsExport([log({ body: 'connection refused' })])
    const body = out.resourceLogs[0]!.scopeLogs[0]!.logRecords[0]!.body
    expect(body).toEqual({ stringValue: 'connection refused' })
  })

  it('threads severity and trace/span correlation through', () => {
    const out = buildLogsExport([log({
      severityNumber: 17,
      severityText: 'Error',
      traceId: 'aabbccddeeff00112233445566778899',
      spanId: '0102030405060708'
    })])
    const rec = out.resourceLogs[0]!.scopeLogs[0]!.logRecords[0]!
    expect(rec.severityNumber).toBe(17)
    expect(rec.severityText).toBe('Error')
    expect(rec.traceId).toBe('aabbccddeeff00112233445566778899')
    expect(rec.spanId).toBe('0102030405060708')
  })

  it('encodes attribute values using OTLP AnyValue', () => {
    const out = buildLogsExport([log({
      attributes: {
        s: 'str',
        b: true,
        i: 42,
        f: 3.14,
        arr: ['a', 1],
        obj: { nested: 'v' },
        nope: null
      }
    })])
    const attrs = out.resourceLogs[0]!.scopeLogs[0]!.logRecords[0]!.attributes
    const byKey = Object.fromEntries(attrs.map(a => [a.key, a.value]))
    expect(byKey.s).toEqual({ stringValue: 'str' })
    expect(byKey.b).toEqual({ boolValue: true })
    expect(byKey.i).toEqual({ intValue: '42' })
    expect(byKey.f).toEqual({ doubleValue: 3.14 })
    expect(byKey.arr).toEqual({ arrayValue: { values: [{ stringValue: 'a' }, { intValue: '1' }] } })
    expect(byKey.obj).toEqual({ kvlistValue: { values: [{ key: 'nested', value: { stringValue: 'v' } }] } })
    // null/undefined attributes are dropped so the OTLP value stays well-formed.
    expect(byKey.nope).toBeUndefined()
  })
})

describe('buildSpansExport', () => {
  it('wraps the result in an OTLP envelope', () => {
    const out = buildSpansExport([{ traceId: 't1', spans: [span()] }])
    expect(out).toHaveProperty('resourceSpans')
  })

  it('groups spans by serviceName then by scope', () => {
    const out = buildSpansExport([
      { traceId: 't1', spans: [
        span({ serviceName: 'svc-a', scopeName: 's1' }),
        span({ serviceName: 'svc-a', scopeName: 's2', spanId: '0a' }),
        span({ serviceName: 'svc-b', scopeName: 's1', spanId: '0b' })
      ] }
    ])
    expect(out.resourceSpans).toHaveLength(2)
    const a = out.resourceSpans.find(r =>
      r.resource.attributes.some(a => a.key === 'service.name' && a.value.stringValue === 'svc-a'))!
    expect(a.scopeSpans).toHaveLength(2)
  })

  it('tags every emitted span with its trace id', () => {
    const out = buildSpansExport([
      { traceId: 'ta', spans: [span({ spanId: 's-a' })] },
      { traceId: 'tb', spans: [span({ spanId: 's-b' })] }
    ])
    const allSpans = out.resourceSpans.flatMap(r => r.scopeSpans.flatMap(s => s.spans))
    const idMap = Object.fromEntries(allSpans.map(s => [s.spanId, s.traceId]))
    expect(idMap['s-a']).toBe('ta')
    expect(idMap['s-b']).toBe('tb')
  })

  it('maps span kind and status to OTLP enum names', () => {
    const out = buildSpansExport([{
      traceId: 't1',
      spans: [
        span({ spanId: 's1', kind: 'Server', statusCode: 'Ok' }),
        span({ spanId: 's2', kind: 'Client', statusCode: 'Error', statusMessage: 'boom' }),
        span({ spanId: 's3', kind: 'Internal', statusCode: 'Unset' })
      ]
    }])
    const allSpans = out.resourceSpans.flatMap(r => r.scopeSpans.flatMap(s => s.spans))
    const byId = Object.fromEntries(allSpans.map(s => [s.spanId, s]))
    expect(byId.s1!.kind).toBe('SPAN_KIND_SERVER')
    expect(byId.s1!.status).toEqual({ code: 'STATUS_CODE_OK' })
    expect(byId.s2!.kind).toBe('SPAN_KIND_CLIENT')
    expect(byId.s2!.status).toEqual({ code: 'STATUS_CODE_ERROR', message: 'boom' })
    expect(byId.s3!.status).toEqual({ code: 'STATUS_CODE_UNSET' })
  })

  it('passes start/end as nanosecond strings', () => {
    const out = buildSpansExport([{
      traceId: 't1',
      spans: [span({ start: '2026-05-23T12:00:00.000Z', end: '2026-05-23T12:00:01.000Z' })]
    }])
    const s = out.resourceSpans[0]!.scopeSpans[0]!.spans[0]!
    expect(typeof s.startTimeUnixNano).toBe('string')
    expect(typeof s.endTimeUnixNano).toBe('string')
    expect(BigInt(s.endTimeUnixNano) - BigInt(s.startTimeUnixNano)).toBe(1_000_000_000n)
  })

  it('copies events and links verbatim, encoding attributes as KeyValue', () => {
    const out = buildSpansExport([{
      traceId: 't1',
      spans: [span({
        events: [{ name: 'log', time: '2026-05-23T12:00:00.500Z', attributes: { x: 1 } }],
        links: [{ traceId: 't2', spanId: 'sx', attributes: { rel: 'follows' } }]
      })]
    }])
    const s = out.resourceSpans[0]!.scopeSpans[0]!.spans[0]!
    expect(s.events).toEqual([
      { timeUnixNano: '1779537600500000000', name: 'log', attributes: [{ key: 'x', value: { intValue: '1' } }] }
    ])
    expect(s.links).toEqual([
      { traceId: 't2', spanId: 'sx', attributes: [{ key: 'rel', value: { stringValue: 'follows' } }] }
    ])
  })
})
