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

import type { LogRecordDto, SpanDto, TraceSummaryDto } from '~/services/types'
import type { TraceSpans } from './otlpExport'

// --- logfmt --------------------------------------------------------------

/**
 * Render a flat list of log records as logfmt — one line per record. Field
 * order is fixed (time → level → service → scope → correlation → message)
 * so a grep-friendly column-ish layout falls out for free when you align
 * columns in a text viewer.
 *
 * Structured attributes are intentionally *not* emitted. The `msg` body is
 * already the substituted form (e.g. "counter set to 894"), so the
 * attribute map mostly carries either values that are already inside the
 * message text (`Value=894`), the raw template (`{OriginalFormat}`), or
 * enrichment that duplicates the top-level columns (`SpanId`, `TraceId`).
 * Listing them inflates each line with noise. Callers who need the
 * structured shape use the OTLP/JSON export, which round-trips verbatim.
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

// --- CSV (RFC 4180) ------------------------------------------------------

/**
 * Render the logs grid as CSV. Columns mirror the on-screen table so the
 * file matches "what I'm looking at": time, service, severity, scope,
 * trace_id, span_id, body. Nested attribute structures are not surfaced —
 * use the OTLP/JSON or logfmt export for those.
 */
export function buildLogsCsv(logs: LogRecordDto[]): string {
  const header = ['time', 'service', 'severity', 'scope', 'trace_id', 'span_id', 'body']
  const rows = logs.map(l => [
    l.time,
    l.serviceName ?? '',
    l.severityText ?? (l.severityNumber > 0 ? String(l.severityNumber) : ''),
    l.scopeName ?? '',
    l.traceId ?? '',
    l.spanId ?? '',
    l.body ?? ''
  ])
  return formatCsv(header, rows)
}

/**
 * Render the traces grid as CSV. Columns mirror the on-screen table:
 * start, service, root_span, duration_ms, spans, status, trace_id. Per-span
 * detail is intentionally omitted — for that, drill into a single trace and
 * use the tree-text or OTLP export.
 */
export function buildTracesCsv(traces: TraceSummaryDto[]): string {
  const header = ['start', 'service', 'root_span', 'duration_ms', 'spans', 'status', 'trace_id']
  const rows = traces.map(t => [
    t.start,
    t.serviceName ?? '',
    t.rootSpanName,
    String(t.durationMs),
    String(t.spanCount),
    t.rootStatusCode,
    t.traceId
  ])
  return formatCsv(header, rows)
}

function formatCsv(header: string[], rows: string[][]): string {
  // LF line endings — modern spreadsheet tools (Excel since 2010, Numbers,
  // Sheets) all accept them, and the file ends up ~50% smaller than CRLF
  // on chatty log exports. The header row is always present so empty
  // exports still produce a valid two-line file.
  const lines = [header.map(csvField).join(',')]
  for (const row of rows) lines.push(row.map(csvField).join(','))
  return lines.join('\n') + '\n'
}

/** RFC 4180 field quoting: wrap when the value contains a comma, quote,
 *  CR, or LF; double internal quotes. Bare value otherwise — keeps small
 *  files visually compact. */
function csvField(value: string): string {
  if (/[",\r\n]/.test(value)) {
    return `"${value.replace(/"/g, '""')}"`
  }
  return value
}

// --- clipboard / LLM-friendly markdown -----------------------------------
//
// The clipboard exports wrap one of the existing text formats inside a
// short markdown envelope:
//
//   **OtlpDashboard <kind>**
//   <context lines>
//
//   ```<lang>
//   <body>
//   ```
//
// The context block — window, active filters, count — is what makes the
// paste useful in an LLM chat: it tells the model what it's looking at
// without the user having to retype it. Pages assemble their own context
// lines locally (they're the only ones that know the live filter state).

/** Wrap a body payload in the markdown envelope above. `fenceLang` flags
 *  the code block (`log` for logfmt, blank for the trace tree) — most
 *  syntax highlighters ignore unknown tags, but `log` does have shading
 *  in popular themes. */
export function buildClipboardMarkdown(
  title: string,
  contextLines: string[],
  body: string,
  fenceLang = ''
): string {
  const sections = [`**${title}**`, ...contextLines, '', `\`\`\`${fenceLang}`, body.replace(/\n+$/, ''), '```']
  return sections.join('\n') + '\n'
}

/**
 * One-line-per-trace summary for the traces-list clipboard export. Compact
 * on purpose — the alternative (a tree per trace) would either need N
 * detail fetches or blow up the token budget. Fields mirror what the user
 * scans in the table: `<id>  <root>  [<status> <duration> <spans>]  service=...`.
 */
export function buildTracesSummaryList(traces: TraceSummaryDto[]): string {
  const lines = traces.map(t => {
    const idShort = t.traceId.length > 16 ? `${t.traceId.slice(0, 16)}…` : t.traceId
    const meta = `[${t.rootStatusCode.toLowerCase()} ${formatDuration(t.durationMs)} ${t.spanCount} spans]`
    const tail = t.serviceName ? `  ${kv('service', t.serviceName)}` : ''
    return `- ${idShort}  ${t.rootSpanName}  ${meta}${tail}`
  })
  return lines.join('\n') + (lines.length > 0 ? '\n' : '')
}

/** Write `text` to the system clipboard. Returns `true` on success, `false`
 *  when the browser blocks the call (insecure context, missing permission,
 *  etc.) — the caller decides how to surface the failure. */
export async function copyToClipboard(text: string): Promise<boolean> {
  if (typeof navigator === 'undefined' || !navigator.clipboard) return false
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return false
  }
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
