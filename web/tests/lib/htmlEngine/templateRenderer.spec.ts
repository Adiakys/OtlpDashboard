import { describe, expect, it } from 'vitest'
import { renderTemplate, TemplateError } from '~/lib/htmlEngine/templateRenderer'

describe('renderTemplate — interpolation', () => {
  it('renders static text untouched', () => {
    expect(renderTemplate('hello world', {})).toBe('hello world')
  })

  it('interpolates a top-level value', () => {
    expect(renderTemplate('value={{ x }}', { x: 42 })).toBe('value=42')
  })

  it('walks a dot-path', () => {
    expect(renderTemplate('{{ load.value }}', { load: { value: 87.5 } }))
      .toBe('87.5')
  })

  it('renders empty for missing paths', () => {
    expect(renderTemplate('a={{ missing }};b={{ also.deeply.missing }}', {}))
      .toBe('a=;b=')
  })

  it('escapes HTML in interpolated strings', () => {
    expect(renderTemplate('{{ s }}', { s: '<img onerror=alert(1)>' }))
      .toBe('&lt;img onerror=alert(1)&gt;')
  })

  it('does not dump objects/arrays into the output', () => {
    expect(renderTemplate('{{ obj }}', { obj: { x: 1 } })).toBe('')
    expect(renderTemplate('{{ arr }}', { arr: [1, 2] })).toBe('')
  })
})

describe('renderTemplate — helpers', () => {
  it('calls a helper with positional args (path + literal)', () => {
    const out = renderTemplate('{{ format value "ms" }}', { value: 250 }, {
      helpers: {
        format: (v, kind) => `${v}${kind}`
      }
    })
    expect(out).toBe('250ms')
  })

  it('passes numeric literals through', () => {
    const out = renderTemplate('{{ percent value 0 100 }}', { value: 70 }, {
      helpers: {
        percent: (v, min, max) => {
          const lo = Number(min), hi = Number(max), x = Number(v)
          return ((x - lo) / (hi - lo) * 100).toFixed(0)
        }
      }
    })
    expect(out).toBe('70')
  })

  it('returns empty string when helper throws', () => {
    expect(renderTemplate('{{ boom x }}', { x: 1 }, {
      helpers: { boom: () => { throw new Error('fail') } }
    })).toBe('')
  })

  it('returns empty string when helper is missing but args present', () => {
    expect(renderTemplate('{{ unknownHelper x }}', { x: 1 })).toBe('')
  })

  it('escapes helper output too', () => {
    const out = renderTemplate('{{ identity s }}', { s: '<b>x</b>' }, {
      helpers: { identity: v => v }
    })
    expect(out).toBe('&lt;b&gt;x&lt;/b&gt;')
  })
})

describe('renderTemplate — {{#if}}', () => {
  it('renders block when truthy', () => {
    expect(renderTemplate('{{#if x}}YES{{/if}}', { x: 1 })).toBe('YES')
    expect(renderTemplate('{{#if x}}YES{{/if}}', { x: 'hello' })).toBe('YES')
    expect(renderTemplate('{{#if items}}YES{{/if}}', { items: [1] })).toBe('YES')
  })

  it('skips block when falsy', () => {
    expect(renderTemplate('{{#if x}}YES{{/if}}', { x: 0 })).toBe('')
    expect(renderTemplate('{{#if x}}YES{{/if}}', { x: null })).toBe('')
    expect(renderTemplate('{{#if x}}YES{{/if}}', { x: false })).toBe('')
    expect(renderTemplate('{{#if items}}YES{{/if}}', { items: [] })).toBe('')
    expect(renderTemplate('{{#if missing}}YES{{/if}}', {})).toBe('')
  })

  it('evaluates with a helper', () => {
    const out = renderTemplate('{{#if eq status "UP"}}OK{{/if}}', { status: 'UP' }, {
      helpers: { eq: (a, b) => a === b }
    })
    expect(out).toBe('OK')
  })

  it('throws on unbalanced if', () => {
    expect(() => renderTemplate('{{#if x}}', { x: 1 })).toThrow(TemplateError)
  })
})

describe('renderTemplate — {{#each}}', () => {
  it('iterates over an array', () => {
    expect(renderTemplate('{{#each xs as x}}-{{x}}{{/each}}', { xs: [1, 2, 3] }))
      .toBe('-1-2-3')
  })

  it('exposes _index', () => {
    expect(renderTemplate('{{#each xs as x}}{{_index}}={{x}};{{/each}}', { xs: ['a', 'b'] }))
      .toBe('0=a;1=b;')
  })

  it('walks dot-paths inside the loop body', () => {
    const tpl = '{{#each services as s}}<li>{{s.name}}:{{s.status}}</li>{{/each}}'
    const out = renderTemplate(tpl, {
      services: [
        { name: 'redis',  status: 'UP' },
        { name: 'postgres', status: 'DOWN' }
      ]
    })
    expect(out).toBe('<li>redis:UP</li><li>postgres:DOWN</li>')
  })

  it('renders nothing when list is missing or empty', () => {
    expect(renderTemplate('{{#each xs as x}}-{{x}}{{/each}}', {})).toBe('')
    expect(renderTemplate('{{#each xs as x}}-{{x}}{{/each}}', { xs: [] })).toBe('')
  })

  it('caps loop iterations to maxLoopIterations', () => {
    const xs = Array.from({ length: 10 }, (_, i) => i)
    const out = renderTemplate('{{#each xs as x}}{{x}}{{/each}}', { xs }, { maxLoopIterations: 3 })
    expect(out).toBe('012')
  })

  it('throws on missing alias', () => {
    expect(() => renderTemplate('{{#each xs}}{{/each}}', { xs: [1] })).toThrow(TemplateError)
  })
})

describe('renderTemplate — security', () => {
  it('does not eval JS-like content in args', () => {
    // `eval('alert(1)')` looks like a function call but the renderer
    // can't reach `eval` because it's not in the helpers whitelist.
    expect(renderTemplate("{{ eval('alert(1)') }}", {})).toBe('')
  })

  it('protects against scope injection via prototype chain', () => {
    // A naive renderer might walk the prototype to find `toString` and
    // dump '[object Object]'. We strip non-leaf objects via stringify.
    expect(renderTemplate('{{ x }}', { x: Object.create({ injected: 'NO' }) })).toBe('')
  })
})
