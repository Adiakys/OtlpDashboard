/**
 * Rewrite a CSS string so every selector is prefixed with a single
 * scope selector — typically `.widget-html-<id>` — so styles authored
 * by an untrusted widget can't bleed into the rest of the SPA.
 *
 * The implementation deliberately avoids a full CSS parser: it walks
 * top-level rule blocks (skipping nested at-rules like `@media`,
 * `@keyframes`, `@supports`) and prepends the scope to each comma-
 * separated selector group. CSS variables, custom properties,
 * pseudo-elements/classes, and combinators all flow through unchanged.
 *
 * What it deliberately strips:
 *   - `@import`         — no fetching from the widget's CSS.
 *   - `@font-face`      — no remote font loads.
 *   - `behavior:`, `-moz-binding`, `expression(`  — IE-era code-exec.
 *
 * Anything else is best-effort: a malicious `url(javascript:...)` for
 * instance would survive this pass — DOMPurify on the rendered HTML
 * + browser CSP catches the rest of the surface.
 */
export function scopeCss(css: string, scope: string): string {
  if (!css) return ''
  const stripped = stripDangerousAtRules(css)
  return rewrite(stripped, scope)
}

function stripDangerousAtRules(css: string): string {
  let out = css
  // @import / @font-face: drop the entire statement (line-terminated or
  // brace-block).
  out = out.replace(/@import\s+[^;]+;?/gi, '')
  out = out.replace(/@font-face\s*\{[^}]*\}/gi, '')
  // Inline IE-era code-exec relics.
  out = out.replace(/behavior\s*:[^;}]*[;}]?/gi, '')
  out = out.replace(/-moz-binding\s*:[^;}]*[;}]?/gi, '')
  out = out.replace(/expression\s*\([^)]*\)/gi, '')
  return out
}

const NESTED_AT_RULES = /^@(media|supports|keyframes|-webkit-keyframes|layer|container)\b/i

function rewrite(css: string, scope: string): string {
  let out = ''
  let i = 0
  while (i < css.length) {
    const next = css.indexOf('{', i)
    if (next < 0) {
      out += css.slice(i)
      break
    }
    const selectorChunk = css.slice(i, next)
    const block = readBalancedBlock(css, next)
    if (block === null) {
      // Unbalanced — bail out, return what we've prefixed so far. The
      // sanitiser is the safety net.
      out += css.slice(i)
      break
    }
    const trimmed = selectorChunk.trim()
    if (!trimmed) {
      out += selectorChunk + block.text
    } else if (NESTED_AT_RULES.test(trimmed)) {
      // Recurse into the at-rule body so the inner selectors get scoped
      // (e.g. `@media (...) { .x { ... } }` → scope `.x`, leave `@media`
      // intact).
      const inner = block.text.slice(1, -1) // strip { }
      out += selectorChunk + '{' + rewrite(inner, scope) + '}'
    } else {
      out += prefixSelectors(selectorChunk, scope) + block.text
    }
    i = next + block.text.length
  }
  return out
}

function readBalancedBlock(css: string, start: number): { text: string } | null {
  if (css[start] !== '{') return null
  let depth = 0
  for (let i = start; i < css.length; i++) {
    const c = css[i]
    if (c === '{') depth++
    else if (c === '}') {
      depth--
      if (depth === 0) return { text: css.slice(start, i + 1) }
    }
  }
  return null
}

function prefixSelectors(chunk: string, scope: string): string {
  const parts = chunk.split(',')
  const prefixed = parts.map(p => {
    const trimmed = p.trim()
    if (!trimmed) return p
    // `:host`-style selectors stay anchored to the scope element only.
    if (trimmed.startsWith(':root') || trimmed.startsWith(':host')) {
      return p.replace(trimmed, scope)
    }
    // Already-prefixed selectors (rare but possible if an author copied
    // pre-scoped CSS in) — leave them.
    if (trimmed.startsWith(scope)) return p
    return p.replace(trimmed, `${scope} ${trimmed}`)
  })
  return prefixed.join(',')
}
