/**
 * Mustache-light template renderer for the `html` widget engine.
 *
 * Supported syntax (intentionally narrow — every construct here is
 * parseable without a JS interpreter so user-authored templates stay
 * non-executable):
 *
 *   {{ name }}                 — interpolate a value from the scope
 *   {{ name.path.to.field }}   — dot-path lookup
 *   {{ helper arg1 arg2 }}     — call a whitelisted helper
 *   {{#if condExpr}}…{{/if}}   — render block when condExpr is truthy
 *   {{#each list as item}}…{{/each}}  — iterate, expose `item` and `_index`
 *
 * Argument literals: numbers (`42`, `3.14`), single/double quoted
 * strings (`'ms'`, `"foo"`), `true`, `false`, `null`. Anything else is
 * resolved as a dot-path against the current scope.
 *
 * Output is HTML-escaped at every interpolation site (helpers can
 * return raw strings — they're escaped too). DOMPurify runs on the
 * final HTML downstream, so even if a helper returns markup the
 * sanitizer is the authoritative gate.
 */

export type Helper = (...args: unknown[]) => unknown

export type Scope = Record<string, unknown>

export interface RenderOptions {
  helpers?: Record<string, Helper>
  /** Soft cap on each `{{#each}}` iteration count to keep a malicious
   *  spec from exploding the render. Default 5_000. */
  maxLoopIterations?: number
}

// =============================================================
// AST
// =============================================================

interface StaticNode { kind: 'static'; text: string }
interface ExprNode   { kind: 'expr'; expr: Expression }
interface IfNode     { kind: 'if'; cond: Expression; body: Node[] }
interface EachNode   { kind: 'each'; list: Expression; alias: string; body: Node[] }
type Node = StaticNode | ExprNode | IfNode | EachNode

interface Expression {
  /** Either a helper name or the leading dot-path of a value lookup. */
  head: Arg
  args: Arg[]
}

type Arg =
  | { kind: 'literal'; value: unknown }
  | { kind: 'path'; path: string[] }

// =============================================================
// Public API
// =============================================================

export function renderTemplate(
  template: string,
  scope: Scope,
  options: RenderOptions = {}
): string {
  const ast = parse(template)
  const ctx: RenderCtx = {
    helpers: options.helpers ?? {},
    maxLoopIterations: options.maxLoopIterations ?? 5_000
  }
  return renderNodes(ast, scope, ctx)
}

// =============================================================
// Parser — tokenize {{ ... }} segments, walk into a tree.
// =============================================================

function parse(template: string): Node[] {
  const tokens = tokenize(template)
  const { nodes, consumed } = buildBlock(tokens, 0, null)
  if (consumed !== tokens.length) {
    throw new TemplateError('Unbalanced template: extra closing tag.')
  }
  return nodes
}

interface Token {
  kind: 'static' | 'expr' | 'if' | 'endif' | 'each' | 'endeach'
  text: string
  /** Only set for `expr` / `if` / `each`. */
  body?: string
  /** Only set for `each`: the alias declared via `as`. */
  alias?: string
}

const BLOCK_RE = /\{\{\s*([\s\S]*?)\s*\}\}/g

function tokenize(template: string): Token[] {
  const out: Token[] = []
  let lastIndex = 0
  let m: RegExpExecArray | null

  // Reset stateful regex (top-level `BLOCK_RE` reused across calls).
  BLOCK_RE.lastIndex = 0

  while ((m = BLOCK_RE.exec(template)) !== null) {
    if (m.index > lastIndex) {
      out.push({ kind: 'static', text: template.slice(lastIndex, m.index) })
    }
    const inner = m[1]!.trim()
    if (inner.startsWith('#if ')) {
      out.push({ kind: 'if', text: m[0]!, body: inner.slice(4).trim() })
    } else if (inner === '/if') {
      out.push({ kind: 'endif', text: m[0]! })
    } else if (inner.startsWith('#each ')) {
      // "{{#each list as alias}}" — split on " as ".
      const rest = inner.slice(6).trim()
      const asIdx = rest.lastIndexOf(' as ')
      if (asIdx < 0) throw new TemplateError(`Missing ' as <alias>' in {{#each}}: ${m[0]}`)
      const listExpr = rest.slice(0, asIdx).trim()
      const alias = rest.slice(asIdx + 4).trim()
      if (!/^[a-zA-Z_][\w$]*$/.test(alias)) {
        throw new TemplateError(`Invalid each alias: ${alias}`)
      }
      out.push({ kind: 'each', text: m[0]!, body: listExpr, alias })
    } else if (inner === '/each') {
      out.push({ kind: 'endeach', text: m[0]! })
    } else {
      out.push({ kind: 'expr', text: m[0]!, body: inner })
    }
    lastIndex = m.index + m[0]!.length
  }
  if (lastIndex < template.length) {
    out.push({ kind: 'static', text: template.slice(lastIndex) })
  }
  return out
}

interface BlockResult { nodes: Node[]; consumed: number }

function buildBlock(
  tokens: Token[],
  start: number,
  expectClose: 'endif' | 'endeach' | null
): BlockResult {
  const nodes: Node[] = []
  let i = start
  while (i < tokens.length) {
    const tok = tokens[i]!
    if (tok.kind === 'static') {
      nodes.push({ kind: 'static', text: tok.text })
      i++
    } else if (tok.kind === 'expr') {
      nodes.push({ kind: 'expr', expr: parseExpression(tok.body!) })
      i++
    } else if (tok.kind === 'if') {
      const inner = buildBlock(tokens, i + 1, 'endif')
      nodes.push({ kind: 'if', cond: parseExpression(tok.body!), body: inner.nodes })
      i += inner.consumed + 1
    } else if (tok.kind === 'each') {
      const inner = buildBlock(tokens, i + 1, 'endeach')
      nodes.push({
        kind: 'each',
        list: parseExpression(tok.body!),
        alias: tok.alias!,
        body: inner.nodes
      })
      i += inner.consumed + 1
    } else if (tok.kind === 'endif' || tok.kind === 'endeach') {
      if (tok.kind === expectClose) {
        return { nodes, consumed: i - start + 1 }
      }
      throw new TemplateError(`Unexpected ${tok.text} (expected ${expectClose ?? 'EOF'})`)
    } else {
      i++
    }
  }
  if (expectClose !== null) {
    throw new TemplateError(`Missing closing tag for {{#${expectClose === 'endif' ? 'if' : 'each'}}}`)
  }
  return { nodes, consumed: i - start }
}

function parseExpression(body: string): Expression {
  const tokens = tokenizeExpr(body)
  if (tokens.length === 0) throw new TemplateError(`Empty expression: {{ ${body} }}`)
  const head = parseArg(tokens[0]!)
  const args = tokens.slice(1).map(parseArg)
  return { head, args }
}

/**
 * Whitespace-tokenize an expression body, but keep quoted strings
 * (single / double) intact even if they contain spaces.
 */
function tokenizeExpr(body: string): string[] {
  const out: string[] = []
  let buf = ''
  let quote: '"' | "'" | null = null
  for (let i = 0; i < body.length; i++) {
    const c = body[i]
    if (quote) {
      buf += c
      if (c === quote && body[i - 1] !== '\\') quote = null
    } else if (c === '"' || c === "'") {
      quote = c
      buf += c
    } else if (c === ' ' || c === '\t' || c === '\n') {
      if (buf) { out.push(buf); buf = '' }
    } else {
      buf += c
    }
  }
  if (buf) out.push(buf)
  if (quote) throw new TemplateError(`Unterminated string literal in expression: ${body}`)
  return out
}

function parseArg(tok: string): Arg {
  // Numeric literal.
  if (/^-?\d+(\.\d+)?$/.test(tok)) {
    return { kind: 'literal', value: Number(tok) }
  }
  if (tok === 'true')  return { kind: 'literal', value: true }
  if (tok === 'false') return { kind: 'literal', value: false }
  if (tok === 'null')  return { kind: 'literal', value: null }
  // String literal.
  if ((tok.startsWith("'") && tok.endsWith("'")) || (tok.startsWith('"') && tok.endsWith('"'))) {
    return { kind: 'literal', value: tok.slice(1, -1) }
  }
  // Dot-path otherwise.
  return { kind: 'path', path: tok.split('.').filter(Boolean) }
}

// =============================================================
// Renderer
// =============================================================

interface RenderCtx {
  helpers: Record<string, Helper>
  maxLoopIterations: number
}

function renderNodes(nodes: Node[], scope: Scope, ctx: RenderCtx): string {
  let out = ''
  for (const node of nodes) {
    if (node.kind === 'static') {
      out += node.text
    } else if (node.kind === 'expr') {
      const value = evalExpression(node.expr, scope, ctx)
      out += escapeHtml(stringify(value))
    } else if (node.kind === 'if') {
      const cond = evalExpression(node.cond, scope, ctx)
      if (truthy(cond)) {
        out += renderNodes(node.body, scope, ctx)
      }
    } else if (node.kind === 'each') {
      const list = evalExpression(node.list, scope, ctx)
      if (Array.isArray(list)) {
        let i = 0
        for (const item of list) {
          if (i >= ctx.maxLoopIterations) break
          out += renderNodes(node.body, { ...scope, [node.alias]: item, _index: i }, ctx)
          i++
        }
      }
    }
  }
  return out
}

function evalExpression(expr: Expression, scope: Scope, ctx: RenderCtx): unknown {
  // Helper call: head is a path of a single segment that matches a helper name.
  if (expr.head.kind === 'path' && expr.head.path.length === 1) {
    const headName = expr.head.path[0]!
    const helper = ctx.helpers[headName]
    if (helper && expr.args.length > 0) {
      const args = expr.args.map(a => evalArg(a, scope))
      try {
        return helper(...args)
      } catch {
        return ''
      }
    }
  }
  // Plain interpolation: head is the value, no args.
  if (expr.args.length === 0) {
    return evalArg(expr.head, scope)
  }
  // Args present but head not a helper name → still treat as call (helper
  // missing). Returning empty string keeps the placeholder absent rather
  // than dumping a raw object into the output.
  return ''
}

function evalArg(arg: Arg, scope: Scope): unknown {
  if (arg.kind === 'literal') return arg.value
  return resolvePath(scope, arg.path)
}

function resolvePath(scope: Scope, path: string[]): unknown {
  let cur: unknown = scope
  for (const seg of path) {
    if (cur === null || cur === undefined) return undefined
    if (typeof cur !== 'object') return undefined
    cur = (cur as Record<string, unknown>)[seg]
  }
  return cur
}

// =============================================================
// Helpers
// =============================================================

function truthy(value: unknown): boolean {
  if (value === null || value === undefined) return false
  if (value === false) return false
  if (value === 0) return false
  if (value === '') return false
  if (Array.isArray(value) && value.length === 0) return false
  return true
}

function stringify(value: unknown): string {
  if (value === null || value === undefined) return ''
  if (typeof value === 'string') return value
  if (typeof value === 'number') return Number.isFinite(value) ? String(value) : ''
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  // Objects/arrays: don't dump JSON to the DOM. Templates that need a
  // structured value should iterate it via {{#each}} or pull a leaf.
  return ''
}

const ESCAPE_MAP: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;'
}

function escapeHtml(s: string): string {
  return s.replace(/[&<>"']/g, c => ESCAPE_MAP[c]!)
}

// =============================================================
// Errors
// =============================================================

export class TemplateError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'TemplateError'
  }
}
