import { describe, expect, it } from 'vitest'
import { TEMPLATE_HELPERS } from '~/lib/htmlEngine/helpers'

describe('helpers — format', () => {
  const { format } = TEMPLATE_HELPERS

  it('formats with unit kind', () => {
    expect(format(250, 'ms')).toMatch(/250.*ms/)
  })

  it('returns empty for non-numeric input', () => {
    expect(format('abc', 'ms')).toBe('')
    expect(format(NaN, 'ms')).toBe('')
  })

  it('falls back to plain string when kind is missing', () => {
    expect(format(42)).toBe('42')
  })
})

describe('helpers — percent', () => {
  const { percent } = TEMPLATE_HELPERS

  it('clamps into [0, 100]', () => {
    expect(percent(50, 0, 100)).toBe('50.0')
    expect(percent(150, 0, 100)).toBe('100.0')
    expect(percent(-10, 0, 100)).toBe('0.0')
  })

  it('handles inverted ranges and degenerate input', () => {
    // Inverted range linearises symmetrically — `(50 - 100)/(0 - 100) = 0.5`.
    expect(percent(50, 100, 0)).toBe('50.0')
    expect(percent(50, 50, 50)).toBe('0')        // zero range guard
    expect(percent('x', 0, 100)).toBe('0')
  })
})

describe('helpers — comparators', () => {
  const { eq, neq, lt, lte, gt, gte } = TEMPLATE_HELPERS

  it('eq / neq compare loosely', () => {
    expect(eq('1', 1)).toBe(true)
    expect(neq('1', 2)).toBe(true)
    expect(eq(null, undefined)).toBe(false)
  })

  it('numeric comparators coerce', () => {
    expect(lt('5', '10')).toBe(true)
    expect(gte(10, 10)).toBe(true)
    expect(gt('foo', 1)).toBe(false)
    expect(lte(NaN, 1)).toBe(false)
  })
})

describe('helpers — dateAgo', () => {
  const { dateAgo } = TEMPLATE_HELPERS

  it('returns "Ns ago" within the minute', () => {
    const t = new Date(Date.now() - 30_000).toISOString()
    expect(dateAgo(t)).toMatch(/^\d+s ago$/)
  })

  it('returns "Nm ago" within the hour', () => {
    const t = Date.now() - 5 * 60_000
    expect(dateAgo(t)).toMatch(/^\d+m ago$/)
  })

  it('handles unix-seconds magnitudes', () => {
    const tSec = Math.floor(Date.now() / 1000) - 10
    expect(dateAgo(tSec)).toMatch(/^\d+s ago$/)
  })

  it('returns empty for malformed input', () => {
    expect(dateAgo('not-a-date')).toBe('')
    expect(dateAgo(null)).toBe('')
  })
})

describe('helpers — pluralize', () => {
  const { pluralize } = TEMPLATE_HELPERS

  it('picks singular for 1, plural otherwise', () => {
    expect(pluralize(1, 'item', 'items')).toBe('item')
    expect(pluralize(2, 'item', 'items')).toBe('items')
    expect(pluralize(0, 'item', 'items')).toBe('items')
  })
})

describe('helpers — thresholdColor / thresholdClass', () => {
  const { thresholdColor, thresholdClass } = TEMPLATE_HELPERS
  const thresholds = [
    { value: 0,   color: '#7AAA7A' },
    { value: 200, color: '#D9B566' },
    { value: 500, color: '#E27A3F' }
  ]

  it('thresholdColor returns the matching stop colour', () => {
    expect(thresholdColor(50, thresholds)).toBe('#7AAA7A')
    expect(thresholdColor(300, thresholds)).toBe('#D9B566')
    expect(thresholdColor(1000, thresholds)).toBe('#E27A3F')
  })

  it('thresholdClass derives a tone class', () => {
    expect(thresholdClass(50, thresholds)).toBe('vellum-th-ok')
    expect(thresholdClass(300, thresholds)).toBe('vellum-th-warn')
    expect(thresholdClass(1000, thresholds)).toBe('vellum-th-bad')
  })

  it('returns empty for missing thresholds', () => {
    expect(thresholdColor(50, undefined)).toBe('')
    expect(thresholdClass(50, undefined)).toBe('')
  })
})

describe('helpers — default', () => {
  const { default: dflt } = TEMPLATE_HELPERS

  it('returns the first non-empty argument', () => {
    expect(dflt(null, '', 'fallback')).toBe('fallback')
    expect(dflt(undefined, 0, 'x')).toBe(0)
  })

  it('returns empty when all args are nullish', () => {
    expect(dflt(null, undefined, '')).toBe('')
  })
})
