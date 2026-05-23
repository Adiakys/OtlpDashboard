// Compact, human-readable export formats.
//
// Logs use `logfmt` (Heroku/Grafana/Loki convention): `key=value` pairs
// separated by spaces, with values quoted only when ambiguous. Traces use
// an indented ASCII tree — token-cheap for LLM consumption while staying
// readable to a human scanning a terminal.
//
// Both formats are lossy with respect to OTLP: nested attribute structures
// are flattened, some type nuances (int vs double) collapse to text. They
// are intended as a *read* path (paste into an LLM, eyeball, grep), not as
// a round-trippable wire format — for that, use the OTLP/JSON export.

import type { LogRecordDto, SpanDto } from '~/services/types'
import type { TraceSpans } from './otlpExport'

// --- logfmt --------------------------------------------------------------

/**
 * Render a flat list of log records as logfmt — one line per record. Field
 * order is fixed (time → level → service → scope → correlation → message →
 * attributes) so a grep-friendly column-ish layout falls out for free when
 * you align columns in a text viewer.
 */
export function buildLogfmt(logs: LogRecordDto[]): string {
  const lines: string[] = []
  for (const log of logs) {
    const parts: string[] = []
    parts.push(kv('time', log.time))
    if (log.severityText) parts.push(kv('level', log.severityText))
    else if (log.severityNumber > 0) parts.push(kv('level', String(log.severityNumber)))
    if (log.serviceName) parts.push(kv('service', log.serviceName))
    if (log.scopeName) parts.push(kv('scope', log.scopeName))
    if (log.traceId) parts.push(kv('trace_id', log.traceId))
    if (log.spanId) parts.push(kv('span_id', log.spanId))
    if (log.body !== null && log.body !== undefined) parts.push(kv('msg', log.body))
    // Attributes are namespaced under `attr.` so they don't collide with
    // the well-known top-level keys (`time`, `level`, etc.). Nested values
    // are flattened to JSON-ish text — logfmt is a flat key=value protocol
    // by design.
    for (const [k, v] of Object.entries(log.attributes ?? {})) {
      if (v === null || v === undefined) continue
      parts.push(kv(`attr.${k}`, formatValue(v)))
    }
    lines.push(parts.join(' '))
  }
  return lines.join('\n') + (lines.length > 0 ? '\n' : '')
}

function kv(key: string, value: string): string {
  return `${key}=${quoteIfNeeded(value)}`
}

/**
 * Quote per logfmt rules: bare when the value is non-empty and contains
 * none of `space " = \\ \n \r`, quoted otherwise. Inside quotes, escape
 * backslash and double-quote so the value round-trips through any
 * standard logfmt parser.
 */
function quoteIfNeeded(value: string): string {
  if (value === '') return '""'
  if (/[\s"=\\]/.test(value)) {
    return `"${value.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n').replace(/\r/g, '\\r')}"`
  }
  return value
}

function formatValue(v: unknown): string {
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean' || typeof v === 'bigint') return String(v)
  // Objects / arrays don't have a sensible flat shape — fall back to JSON
  // so the value is at least machine-parseable downstream.
  try {
    return JSON.stringify(v)
  } catch {
    return String(v)
  }
}

// --- trace tree ----------------------------------------------------------

/**
 * Render a single trace as an indented ASCII tree. Pure ASCII (no
 * box-drawing characters) — keeps the file LLM-friendly: each indent step
 * is two spaces + `- `, which most tokenisers fold into a single token.
 *
 * Layout per line:
 *   {indent}- {name}  [{kind} {status} {duration}]  service={svc}  attr.k=v ...
 *
 * Orphan spans (parent not in the slice) are emitted at depth 0 so nothing
 * disappears from a truncated trace.
 */
export function buildTraceTree(trace: TraceSpans): string {
  const lines: string[] = []
  lines.push(traceHeader(trace))
  if (trace.spans.length === 0) {
    lines.push('  (no spans)')
    return lines.join('\n') + '\n'
  }

  // Build child-index once: O(N) walk, then a depth-first emit.
  const byParent = new Map<string | null, SpanDto[]>()
  const ids = new Set<string>()
  for (const s of trace.spans) ids.add(s.spanId)
  for (const s of trace.spans) {
    const parent = s.parentSpanId && ids.has(s.parentSpanId) ? s.parentSpanId : null
    const bucket = byParent.get(parent) ?? []
    bucket.push(s)
    byParent.set(parent, bucket)
  }
  // Stable order by start time within each sibling group — gives a
  // chronological flow that's easy to follow when scanning a deep tree.
  for (const list of byParent.values()) {
    list.sort((a, b) => a.start.localeCompare(b.start))
  }

  function emit(span: SpanDto, depth: number) {
    lines.push(formatSpanLine(span, depth))
    const children = byParent.get(span.spanId)
    if (children) {
      for (const c of children) emit(c, depth + 1)
    }
  }

  for (const root of byParent.get(null) ?? []) emit(root, 0)
  return lines.join('\n') + '\n'
}

/**
 * Render a list of traces as a series of trees, separated by a blank line.
 * Each tree carries its own one-line header (`trace=… duration=… spans=…`)
 * so a slice of the output stays meaningful on its own.
 */
export function buildTraceTrees(traces: TraceSpans[]): string {
  return traces.map(buildTraceTree).join('\n')
}

function traceHeader(trace: TraceSpans): string {
  if (trace.spans.length === 0) {
    return `trace=${trace.traceId} spans=0`
  }
  let minStart = trace.spans[0]!.start
  let maxEnd = trace.spans[0]!.end
  for (const s of trace.spans) {
    if (s.start < minStart) minStart = s.start
    if (s.end > maxEnd) maxEnd = s.end
  }
  const durationMs = new Date(maxEnd).getTime() - new Date(minStart).getTime()
  return `trace=${trace.traceId} start=${minStart} duration=${formatDuration(durationMs)} spans=${trace.spans.length}`
}

function formatSpanLine(span: SpanDto, depth: number): string {
  const indent = '  '.repeat(depth)
  const meta = `[${span.kind.toLowerCase()} ${span.statusCode.toLowerCase()} ${formatDuration(span.durationMs)}]`
  const tail: string[] = []
  if (span.serviceName) tail.push(kv('service', span.serviceName))
  if (span.statusMessage) tail.push(kv('error', span.statusMessage))
  for (const [k, v] of Object.entries(span.attributes ?? {})) {
    if (v === null || v === undefined) continue
    tail.push(kv(`attr.${k}`, formatValue(v)))
  }
  return `${indent}- ${span.name}  ${meta}${tail.length > 0 ? '  ' + tail.join(' ') : ''}`
}

function formatDuration(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return '?'
  if (ms < 1) return `${(ms * 1000).toFixed(0)}us`
  if (ms < 1000) return `${ms.toFixed(1)}ms`
  return `${(ms / 1000).toFixed(2)}s`
}

// --- file download -------------------------------------------------------

/** Trigger a browser download for a text payload. Extension drives the
 *  filename suffix; content-type stays `text/plain` so editors and the
 *  browser preview both render it inline. */
export function downloadText(content: string, baseName: string, extension: string): void {
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = `${baseName}-${todayStamp()}.${extension}`
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function todayStamp(): string {
  return new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19)
}
